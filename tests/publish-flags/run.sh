#!/usr/bin/env bash
# Fixture for scripts/check-publish-flags.py — the gate that asserts `vars.NUGET_ORG_PUBLISH` is `true`
# wherever a workflow gates a nuget.org publish on it (.github#750, epic #266).
#
# The gate exists because a switch nobody checks is a switch that flips silently. So this fixture spends
# most of its length on the FAILURE legs: it proves the gate goes red when the variable is unset, false, or
# never passed to it — and, critically, that it does NOT fire on a workflow that merely mentions the
# variable without publishing, nor on a repo that gates no publisher at all.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the #266 vacuous-failure class, where a
# "must fail" test's non-zero exit comes from a path guard rather than from the thing under test. `must_fail`
# therefore takes a required pattern.
#
# Throwaway trees under a temp dir, no network (the gate reads local workflow files + one env var).
# Mirrors tests/feed-coherence/run.sh.
set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-publish-flags.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/publish-flags-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# must_pass <name> <env-assignments...> -- <gate args...>
must_pass() {
  local name="$1"; shift
  local out rc=0
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$name"; else bad "$name" "expected exit 0, got $rc: $out"; fi
}

# must_fail <name> <required-pattern> <cmd...> — non-zero AND the reason names the thing under test.
must_fail() {
  local name="$1" needle="$2"; shift 2
  local out rc=0
  out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$name" "expected NON-ZERO exit, got 0: $out"
  elif ! printf '%s' "$out" | grep -qF "$needle"; then
    bad "$name" "exited non-zero but the reason did not mention '$needle': $out"
  else
    ok "$name"
  fi
}

# ---- fixture worlds --------------------------------------------------------------------------------

# A world with ONE policy-gated nuget.org publisher: it names the flag AND publishes (NuGet/login).
GATED="$WORK/gated"
mkdir -p "$GATED"
cat > "$GATED/release-thing.yml" <<'YAML'
name: release-thing
on: { push: { tags: ["v*"] } }
jobs:
  release:
    steps:
      - name: Push to nuget.org
        if: steps.v.outputs.push == 'true' && vars.NUGET_ORG_PUBLISH == 'true'
        uses: NuGet/login@v1
YAML

# A world where the ONLY mention of the flag is PROSE — no nuget.org publish. The gate must not treat this
# as a policy-gated publisher, or it would fire on its own documentation.
PROSE="$WORK/prose"
mkdir -p "$PROSE"
cat > "$PROSE/notes.yml" <<'YAML'
name: notes
on: { workflow_dispatch: {} }
jobs:
  doc:
    steps:
      - run: echo "someday NUGET_ORG_PUBLISH may gate a publish, but this workflow does not publish"
YAML

# A world with an UNCONDITIONAL nuget.org publisher — publishes, but does not gate on the flag. Safe by
# construction (the five product repos are this shape); NOT this gate's subject.
UNCOND="$WORK/uncond"
mkdir -p "$UNCOND"
cat > "$UNCOND/release-uncond.yml" <<'YAML'
name: release-uncond
on: { push: { tags: ["v*"] } }
jobs:
  release:
    steps:
      - name: Push to nuget.org (always)
        uses: NuGet/login@v1
YAML

# ---- the GREEN baseline ----------------------------------------------------------------------------

must_pass "flag=true over a gated publisher is GREEN, and names it" \
  env NUGET_ORG_PUBLISH=true python3 "$GATE" "$GATED"

# ...and the green actually NAMES the workflow it checked (not a silent all-clear).
green_out="$(env NUGET_ORG_PUBLISH=true python3 "$GATE" "$GATED" 2>&1)"
case "$green_out" in
  *release-thing.yml*) ok "the green names the publisher it asserted" ;;
  *) bad "the green names the publisher it asserted" "$green_out" ;;
esac

# ---- the FAILURE legs — the whole point ------------------------------------------------------------

# THE #750 HAZARD: a policy exists and the variable is UNSET (GitHub renders an unset repo var as '').
must_fail "an UNSET flag over a gated publisher is RED (the #750 hazard)" "SILENTLY stop" \
  env NUGET_ORG_PUBLISH= python3 "$GATE" "$GATED"

# ...and it NAMES the workflow that will stop publishing.
must_fail "...and it names the workflow that will stop publishing" "release-thing.yml" \
  env NUGET_ORG_PUBLISH= python3 "$GATE" "$GATED"

# A deliberate 'false' is still red — a policy plus a disabled publish is a producer that silently stops.
must_fail "flag='false' over a gated publisher is RED" "SILENTLY stop" \
  env NUGET_ORG_PUBLISH=false python3 "$GATE" "$GATED"

# NOT WIRED: the variable was never passed to the gate at all. The gate cannot see its subject — fail-open,
# so fail closed instead.
must_fail "an ABSENT flag (never passed to the gate) is RED, not a pass" "not passed to this gate" \
  env -u NUGET_ORG_PUBLISH python3 "$GATE" "$GATED"

# A workflows directory that does not exist must not pass.
must_fail "a missing workflows directory is RED" "not found" \
  env NUGET_ORG_PUBLISH=true python3 "$GATE" "$WORK/does-not-exist"

# ---- the NON-VACUITY legs — the gate must not fire where there is nothing to assert ----------------

# Prose that merely mentions the flag is NOT a publisher: with the flag unset, this is still GREEN, because
# there is no gated publisher to protect. If the gate fired here it would red every repo that documents the
# variable.
must_pass "a prose-only mention of the flag is NOT a gated publisher (green even with the flag unset)" \
  env NUGET_ORG_PUBLISH= python3 "$GATE" "$PROSE"

# An UNCONDITIONAL publisher (publishes, does not gate on the flag) is safe by construction and not asserted.
must_pass "an UNCONDITIONAL nuget.org publisher is not this gate's subject (green with the flag unset)" \
  env NUGET_ORG_PUBLISH= python3 "$GATE" "$UNCOND"

# ---- the REALITY leg — run against THIS repo's actual workflows ------------------------------------

# The gate must find the two real policy-gated publishers (release-coord-engine, release-new-sdd-workspace)
# and pass with the flag true — and, sharply, FAIL naming them if the flag were unset. A fixture that never
# runs against the real tree proves only that the gate agrees with the fixture.
must_pass "against THIS repo's real workflows, flag=true is GREEN" \
  env NUGET_ORG_PUBLISH=true python3 "$GATE" "$REPO_ROOT/.github/workflows"

real_out="$(env NUGET_ORG_PUBLISH=true python3 "$GATE" "$REPO_ROOT/.github/workflows" 2>&1)"
case "$real_out" in
  *release-coord-engine.yml*release-new-sdd-workspace.yml*|*release-new-sdd-workspace.yml*release-coord-engine.yml*)
    ok "the real run names BOTH .github publishers (Coord.Cli + NewSddWorkspace)" ;;
  *) bad "the real run names both .github publishers" "$real_out" ;;
esac

must_fail "against THIS repo's real workflows, an UNSET flag is RED and names them" "release-coord-engine.yml" \
  env NUGET_ORG_PUBLISH= python3 "$GATE" "$REPO_ROOT/.github/workflows"

# ---- report ----------------------------------------------------------------------------------------
echo
echo "publish-flags fixture: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::publish-flags fixture FAILED"; exit 1; }
echo "green."
