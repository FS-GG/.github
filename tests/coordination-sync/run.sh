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

# --- the roster gate fails CLOSED on an unreadable roster (.github#315) ---
# An unreadable registry, a missing yq/jq/python3, or any repos.sh bug prints nothing to stdout. The
# gate must not read that silence as "not a receiver" and skip the drift check green. coordination-sync
# resolves repos.sh relative to its OWN location, so stand up a fake source root to inject a broken one.
#
# Assert on the MESSAGE, not just the rc: a fake root has no kit sources, so a run that passes the gate
# also exits 2 ("canonical kit source missing"). rc alone cannot tell "died at the gate" from "died
# after it", and would stay green if the fix were reverted.
FAKE="$WORK/fakeroot"; mkdir -p "$FAKE/scripts" "$FAKE/registry"
cp "$SYNC" "$FAKE/scripts/coordination-sync"
cp "$REPO_ROOT/registry/repos.yml" "$FAKE/registry/repos.yml"   # present, so the [ -f ] guard passes
FSYNC="$FAKE/scripts/coordination-sync"
# expect_gate <name> <want-rc> <want-stderr-regex> <cmd...>
expect_gate() { local n="$1" want="$2" re="$3"; shift 3; local out rc=0; out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -eq "$want" ] && printf '%s' "$out" | grep -qE "$re"; then ok "$n"
  else bad "$n" "want rc=$want matching /$re/; got rc=$rc: $out"; fi; }

# repos.sh dies (bad registry, absent yq, ...): stdout empty, exit nonzero.
printf '#!/usr/bin/env bash\necho "repos.sh: bad registry" >&2\nexit 2\n' > "$FAKE/scripts/repos.sh"
expect_gate "gate: roster reader that DIES fails closed, not a green skip" 2 \
  'could not read the coordination-kit roster' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# repos.sh succeeds but enumerates nothing: an empty roster for a declared capability is an error,
# not a verdict that every repo is a non-receiver.
printf '#!/usr/bin/env bash\nexit 0\n' > "$FAKE/scripts/repos.sh"
expect_gate "gate: EMPTY roster fails closed, not a green skip" 2 \
  "no repo declares 'receives: coordination-kit'" \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# A healthy roster still classifies both ways — the fix did not turn the gate into an unconditional die.
printf '#!/usr/bin/env bash\nprintf "%%s\\n" FS-GG/FS.GG.SDD\n' > "$FAKE/scripts/repos.sh"
expect_gate "gate: healthy roster still skips a non-receiver (rc 0)" 0 'nothing to do' \
  bash "$FSYNC" --check --repo FS-GG/.github "$RECV"
# ...and a receiver gets PAST the gate: it reaches the kit-source step, which the fake root lacks.
expect_gate "gate: healthy roster still proceeds for a receiver (past the gate)" 2 \
  'canonical kit source missing' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# The repo name is matched literally, not as a regex — '.' must not match any character.
printf '#!/usr/bin/env bash\nprintf "%%s\\n" FS-GG/FSxGG.SDD\n' > "$FAKE/scripts/repos.sh"
expect_gate "gate: roster match is literal, not a regex" 0 'nothing to do' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

echo "coordination-sync fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coordination-sync fixture FAILED"; exit 1; }
echo "coordination-sync fixture — OK"
