#!/usr/bin/env bash
# Fixture for scripts/check-workflow-timeouts.py (.github#541, epic #266).
#
# Offline, and it needs no stub: the gate is static. It reads the working tree and nothing else — no
# API, no network, no credentials — so there is no transport to fake and no retryable verdict to
# exercise. That is a property worth asserting rather than assuming, so the last section proves the
# gate never touches the network by running it with `gh` and `curl` removed from PATH entirely.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the .github#266 vacuous-failure defect was a "must fail" test whose non-zero exit
# came from a path guard rather than from the thing under test — it would have passed against a gate
# that was broken in a completely different way.
#
# The headline leg is REGRESSION: the REAL lock-range-coherence.yml from this working tree, with its
# `timeout-minutes` stripped back out, is the exact file FS.GG.Game#153 adopted. If this gate had
# existed, that is the run it would have red-lighted.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-workflow-timeouts.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/workflow-timeouts-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root> [args…] — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" root="$4"; shift 4
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# root <dir> — a synthetic working tree with an empty workflows dir.
root() { mkdir -p "$1/.github/workflows"; echo "$1"; }

# job <timeout-line> — a normal job, with or without a timeout.
job() {
  { echo "name: w"; echo "on: { push: }"; echo "jobs:"; echo "  j:"
    echo "    runs-on: ubuntu-latest"
    [ -n "${1:-}" ] && echo "    $1"
    echo "    steps: [{ run: 'true' }]"; }
}

# =============================================================================================
# 1. REGRESSION — the real file, the real bug, from the real working tree.
# =============================================================================================
R="$(root "$WORK/regression")"
# The lock-range-coherence.yml FS.GG.Game#153 adopted: this repo's file, timeout stripped back out.
python3 - "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" \
           "$R/.github/workflows/lock-range-coherence.yml" <<'PY'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
stripped = re.sub(r"^\s*timeout-minutes:.*\n", "", text, flags=re.M)
assert stripped != text, "lock-range-coherence.yml has no timeout-minutes to strip — fix the fixture"
open(dst, "w", encoding="utf-8").write(stripped)
PY

expect "REGRESSION #541: the reusable lock-range-coherence.yml, unbounded, is caught" \
  1 "inherits GitHub's default of 360 minutes" "$R"
expect "REGRESSION: and it says WHY a receiver cannot fix it — no caller can supply the bound" \
  1 "no caller can supply a timeout for it" "$R"

# The fix must actually satisfy the gate: the file as this PR ships it.
R2="$(root "$WORK/regression-fixed")"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R2/.github/workflows/"
expect "...and the shipped file, bounded, passes" 0 "ok:" "$R2"

# THE WHOLE SHIPPED SURFACE. Every job of every workflow this PR ships is bounded.
expect "the entire shipped .github/workflows tree satisfies its own gate" 0 "ok:" "$REPO_ROOT"

# =============================================================================================
# 2. The rule, and its mirror image — which together are the content of #541.
# =============================================================================================
RM="$(root "$WORK/missing")"; job "" > "$RM/.github/workflows/w.yml"
expect "a normal job with NO timeout is caught" 1 "inherits GitHub's default of 360 minutes" "$RM"

RB="$(root "$WORK/bounded")"; job "timeout-minutes: 10" > "$RB/.github/workflows/w.yml"
expect "a normal job with a literal timeout passes" 0 "ok:" "$RB"

# A NON-reusable unbounded job is a finding, but must NOT claim a caller could have bounded it.
out="$(python3 "$TOOL" --root "$RM" 2>&1 || true)"
if grep -qF "no caller can supply a timeout" <<<"$out"; then
  bad "the reusable-only message leaked onto a non-reusable workflow" "$out"
else
  ok "the 'no caller can supply' message is reserved for REUSABLE workflows"
fi

# The mirror image: a `uses:` job CANNOT carry a timeout. GitHub rejects the workflow.
RU="$(root "$WORK/uses-bare")"
{ echo "name: w"; echo "on: { push: }"; echo "jobs:"; echo "  c:"
  echo "    uses: ./.github/workflows/other.yml"; } > "$RU/.github/workflows/w.yml"
job "timeout-minutes: 10" > "$RU/.github/workflows/other.yml"
expect "a \`uses:\` job with NO timeout is CORRECT — it must not be flagged" 0 "bound by its callee" "$RU"

RUT="$(root "$WORK/uses-timeout")"
{ echo "name: w"; echo "on: { push: }"; echo "jobs:"; echo "  c:"
  echo "    uses: ./.github/workflows/other.yml"; echo "    timeout-minutes: 10"; } \
  > "$RUT/.github/workflows/w.yml"
job "timeout-minutes: 10" > "$RUT/.github/workflows/other.yml"
expect "a \`uses:\` job WITH a timeout is caught — GitHub rejects the workflow" \
  1 "GitHub REJECTS this" "$RUT"

# =============================================================================================
# 3. What counts as a bound. A literal, positive, under the ceiling — nothing else.
# =============================================================================================
R360="$(root "$WORK/t360")"; job "timeout-minutes: 360" > "$R360/.github/workflows/w.yml"
expect "an explicit 360 is caught — the six-hour hole spelled out longhand is still the hole" \
  1 "GitHub's unbounded default spelled out longhand" "$R360"

ROVER="$(root "$WORK/over")"; job "timeout-minutes: 61" > "$ROVER/.github/workflows/w.yml"
expect "a timeout over the ceiling is caught" 1 "exceeds the ceiling of 60" "$ROVER"
expect "...and --max-minutes raises the ceiling deliberately" 0 "ok:" "$ROVER" --max-minutes 90

RZ="$(root "$WORK/zero")"; job "timeout-minutes: 0" > "$RZ/.github/workflows/w.yml"
expect "a zero timeout is caught" 1 "not a positive number of minutes" "$RZ"

RNEG="$(root "$WORK/neg")"; job "timeout-minutes: -5" > "$RNEG/.github/workflows/w.yml"
expect "a negative timeout is caught" 1 "not a positive number of minutes" "$RNEG"

REXPR="$(root "$WORK/expr")"
job 'timeout-minutes: ${{ inputs.t }}' > "$REXPR/.github/workflows/w.yml"
expect "an EXPRESSION is not a bound this gate can prove — caught, and told to use a literal" \
  1 "is not a literal integer" "$REXPR"

# `True` IS an `int` in Python and equals 1. Without the explicit bool check, `timeout-minutes: true`
# would sail through as a one-minute bound — a green verdict over a workflow GitHub will not accept.
RBOOL="$(root "$WORK/bool")"; job "timeout-minutes: true" > "$RBOOL/.github/workflows/w.yml"
expect "\`timeout-minutes: true\` is not a one-minute bound (bool is an int in Python)" \
  1 "is not a literal integer" "$RBOOL"

# =============================================================================================
# 4. Fail closed. "Nothing to check" is never green (epic #266).
# =============================================================================================
REMPTY="$(root "$WORK/empty")"
expect "an EMPTY workflows dir is exit 3, never a vacuous green" \
  3 "contains no workflow files" "$REMPTY"

expect "a root with no .github/workflows at all is exit 3" \
  3 "is not a directory" "$WORK/nonexistent"

RBAD="$(root "$WORK/unparsable")"
printf 'name: w\non: { push: }\njobs:\n  j:\n   - broken: [\n' > "$RBAD/.github/workflows/w.yml"
expect "an unparsable workflow is exit 3 — not a finding about a timeout" \
  3 "not parsable as YAML" "$RBAD"

RNOJOBS="$(root "$WORK/nojobs")"
printf 'name: w\non: { push: }\n' > "$RNOJOBS/.github/workflows/w.yml"
expect "a workflow with no \`jobs:\` is exit 3 — it cannot run, and it is not a clean audit" \
  3 "declares no \`jobs:\` mapping" "$RNOJOBS"

RBADMAX="$(root "$WORK/badmax")"; job "timeout-minutes: 10" > "$RBADMAX/.github/workflows/w.yml"
expect "a non-positive --max-minutes is exit 3 (a bad invocation), not a verdict" \
  3 "not a positive number of minutes" "$RBADMAX" --max-minutes 0

# One finding must not mask another: the gate reports EVERY unbounded job, not just the first.
RMULTI="$(root "$WORK/multi")"
job "" > "$RMULTI/.github/workflows/a.yml"
job "" > "$RMULTI/.github/workflows/b.yml"
expect "every unbounded job is reported, not just the first" 1 "2 unbounded or illegal job(s)" "$RMULTI"

# =============================================================================================
# 5. `on:` is the YAML 1.1 boolean True. A gate that misses that sees no reusable workflow at all,
#    and would emit the plain message for the one case the issue is actually about.
# =============================================================================================
RRU="$(root "$WORK/reusable")"
{ echo "name: w"; echo "on:"; echo "  workflow_call:"; echo "jobs:"; echo "  j:"
  echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
  > "$RRU/.github/workflows/w.yml"
expect "a REUSABLE workflow's unbounded job gets the message that names the asymmetry" \
  1 "no caller can supply a timeout for it" "$RRU"

# =============================================================================================
# 6. The gate is STATIC — prove it, rather than asserting it in a comment.
# =============================================================================================
NONET="$WORK/nonet"; mkdir -p "$NONET"
for t in gh curl wget; do
  printf '#!/bin/sh\necho "%s: the gate reached the network" >&2\nexit 127\n' "$t" > "$NONET/$t"
  chmod +x "$NONET/$t"
done
if out="$(PATH="$NONET:/usr/bin:/bin" python3 "$TOOL" --root "$REPO_ROOT" 2>&1)"; then
  ok "the gate reaches no network: it passes with gh/curl/wget replaced by traps"
else
  bad "the gate failed with the network trapped — it is not static" "$out"
fi

# =============================================================================================
# 7. The gate's own shipped surface.
# =============================================================================================
if python3 - "$REPO_ROOT/.github/workflows/timeout-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-workflow-timeouts.py" in body, "the gate workflow never runs the gate"
assert "tests/workflow-timeouts/run.sh" in body, "the gate workflow never runs this fixture"
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself"
PY
then ok "the shipped timeout-coherence.yml declares contents: read, bounds its own jobs, and runs both the gate and this fixture"
else bad "the shipped timeout-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "workflow-timeouts fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::workflow-timeouts fixture FAILED"; exit 1; }
echo "workflow-timeouts fixture — OK"
