#!/usr/bin/env bash
# Fixture for scripts/coordination-sync — the coordination-kit distributor/coherence gate (ADR-0019
# slice 2). Proves: apply writes the kit (client + skill in every root, client executable) into a
# fresh receiver; --check passes on a synced receiver and FAILS (exit 1) on a missing or drifted kit
# file; the --repo roster gate skips a non-receiver (the authority .github) and proceeds for a real
# receiver; and the canonical source matches what apply produced. No network — the real .github
# checkout is the canonical source, a throwaway dir is the receiver. Mirrors the other fixtures.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
SYNC="$HERE/../../scripts/coordination-sync"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/coordination-sync-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
RECV="$WORK/receiver"; mkdir -p "$RECV"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }
# expect_rc <name> <want-rc> <cmd...>
expect_rc() { local n="$1" want="$2"; shift 2; local out rc=0; out="$("$@" 2>&1)" || rc=$?;
  if [ "$rc" -eq "$want" ]; then ok "$n"; else bad "$n" "want rc=$want got=$rc: $out"; fi; }

echo "coordination-sync fixture — receiver='$RECV'"

# --- apply writes the full kit ---
bash "$SYNC" "$RECV" >/dev/null
[ -f "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client written"        || bad "apply: client"
[ -x "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client is executable" || bad "apply: client exec bit"
[ -f "$RECV/.claude/skills/cross-repo-coordination/SKILL.md" ]     && ok "apply: skill in .claude root" || bad "apply: .claude skill"
[ -f "$RECV/.agents/skills/cross-repo-coordination/SKILL.md" ]     && ok "apply: skill in .agents root"|| bad "apply: .agents skill"
diff -q "$REPO_ROOT/scripts/fsgg-coord" "$RECV/scripts/fsgg-coord" >/dev/null \
  && ok "apply: client bytes match canonical" || bad "apply: client bytes"
diff -q "$REPO_ROOT/.claude/skills/cross-repo-coordination/SKILL.md" \
        "$RECV/.claude/skills/cross-repo-coordination/SKILL.md" >/dev/null \
  && ok "apply: skill bytes match canonical" || bad "apply: skill bytes"

# --- check passes when coherent ---
expect_rc "check: coherent receiver passes (rc 0)" 0 bash "$SYNC" --check "$RECV"

# --- check fails on drift and on a missing file ---
printf 'tampered\n' >> "$RECV/.claude/skills/cross-repo-coordination/SKILL.md"
expect_rc "check: drifted skill fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null                       # re-sync back to coherent
expect_rc "check: re-synced receiver passes again (rc 0)" 0 bash "$SYNC" --check "$RECV"
rm -f "$RECV/scripts/fsgg-coord"
expect_rc "check: missing client fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null

# --- roster gate ---
out_auth="$(bash "$SYNC" --check --repo FS-GG/.github "$RECV" 2>&1)"; rc_auth=$?
{ [ "$rc_auth" -eq 0 ] && printf '%s' "$out_auth" | grep -q 'nothing to do'; } \
  && ok "gate: authority .github is not a receiver -> skip" || bad "gate: .github skip" "$out_auth"
expect_rc "gate: real receiver FS.GG.SDD proceeds (rc 0)" 0 bash "$SYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

echo "coordination-sync fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coordination-sync fixture FAILED"; exit 1; }
echo "coordination-sync fixture — OK"
