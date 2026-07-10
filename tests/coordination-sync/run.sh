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

# The skill set under test is the REGISTRY's, read the same way the distributor reads it — by `source`
# path, not by id. Naming one skill here is the defect this fixture exists to catch (#338): it proves
# nothing about the others, and stayed green while `check-board` and `pnext-item` went undistributed.
# A repos.sh that dies yields an empty list, so treat "no rows" as a broken fixture, not a vacuous pass.
mapfile -t SKILL_SRCS < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --kind skill \
                            --registry "$REPO_ROOT/registry/repos.yml")
[ "${#SKILL_SRCS[@]}" -gt 0 ] \
  || { echo "::error::fixture: could not read any 'kind: skill' kit row — nothing to assert."; exit 1; }
# The receiver-side directory name is the source's basename, which is what the distributor writes.
SKILLS=(); for src in "${SKILL_SRCS[@]}"; do SKILLS+=("${src##*/}"); done
echo "coordination-sync fixture — registry declares ${#SKILLS[@]} skill(s): ${SKILLS[*]}"

# --- apply writes the full kit ---
bash "$SYNC" "$RECV" >/dev/null
[ -f "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client written"        || bad "apply: client"
[ -x "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client is executable" || bad "apply: client exec bit"
diff -q "$REPO_ROOT/scripts/fsgg-coord" "$RECV/scripts/fsgg-coord" >/dev/null \
  && ok "apply: client bytes match canonical" || bad "apply: client bytes"

# EVERY registered skill, in EVERY root. Register a skill in repos.yml and forget the distributor and
# these go red — where before, `--check` passed on a receiver that lacked it entirely.
for i in "${!SKILLS[@]}"; do
  s="${SKILLS[$i]}"; src="${SKILL_SRCS[$i]}"
  for root in .claude/skills .agents/skills; do
    [ -f "$RECV/$root/$s/SKILL.md" ] && ok "apply: $s in $root" \
      || bad "apply: $s in $root" "declared 'kind: skill' in repos.yml, not distributed"
  done
  # Bytes come from the registry's declared `source`, not from a path rebuilt out of the id.
  diff -q "$REPO_ROOT/$src/SKILL.md" "$RECV/.claude/skills/$s/SKILL.md" >/dev/null \
    && ok "apply: $s bytes match canonical" || bad "apply: $s bytes"
done

# --- check passes when coherent ---
expect_rc "check: coherent receiver passes (rc 0)" 0 bash "$SYNC" --check "$RECV"

# --- check fails on drift and on a missing file ---
# Drift in ANY registered skill must fail, not just the first one the fixture happens to know about.
for s in "${SKILLS[@]}"; do
  printf 'tampered\n' >> "$RECV/.claude/skills/$s/SKILL.md"
  expect_rc "check: drifted skill '$s' fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
  bash "$SYNC" "$RECV" >/dev/null                     # re-sync back to coherent before the next
done
rm -rf "$RECV/.agents/skills/${SKILLS[0]}"
expect_rc "check: missing skill '${SKILLS[0]}' fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
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
# coordination-sync now asks repos.sh two questions (`list` for the roster, `kit` for the skills), so
# from here on a stub must answer both: one that echoed the roster at every call would let a nonsense
# skill list satisfy the assertions below by accident.
stub_repos_sh() {   # stub_repos_sh <list-body> <kit-body>
  { printf '#!/usr/bin/env bash\ncase "$1" in\n'
    printf '  list) %s ;;\n' "$1"
    printf '  kit)  %s ;;\n' "$2"
    printf '  *)    echo "stub: unexpected subcommand $1" >&2; exit 2 ;;\nesac\n'
  } > "$FAKE/scripts/repos.sh"
}
healthy_list='printf "%s\n" FS-GG/FS.GG.SDD'
healthy_kit='printf "%s\n" .claude/skills/cross-repo-coordination'   # `kit` yields SOURCE paths

stub_repos_sh "$healthy_list" "$healthy_kit"
expect_gate "gate: healthy roster still skips a non-receiver (rc 0)" 0 'nothing to do' \
  bash "$FSYNC" --check --repo FS-GG/.github "$RECV"
# ...and a receiver gets PAST the gate: it reaches the kit-source step, which the fake root lacks.
expect_gate "gate: healthy roster still proceeds for a receiver (past the gate)" 2 \
  'canonical kit source missing' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# --- the SKILL LIST fails CLOSED on an unreadable registry (.github#338) ---
# The distributed skill set is read from repos.yml rather than copied into a literal. That read is now
# a coherence gate of its own, and it inherits the roster gate's failure mode: a repos.sh that dies (or
# enumerates nothing) yields an empty list, `managed()` then emits the client alone, and `--check`
# pronounces a receiver holding ZERO skills coherent.
#
# Assert on the MESSAGE, not the rc: once the derivation succeeds the fake root reaches 'canonical kit
# source missing', which is also rc 2 — so rc alone would stay green if the fix were reverted.
stub_repos_sh "$healthy_list" 'echo "repos.sh: bad registry" >&2; exit 2'
expect_gate "skills: reader that DIES fails closed, not a client-only sync" 2 \
  'could not read the kit skill list' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

stub_repos_sh "$healthy_list" 'exit 0'
expect_gate "skills: EMPTY skill list fails closed, not a client-only sync" 2 \
  "no 'kind: skill' kit item declared" \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# The repo name is matched literally, not as a regex — '.' must not match any character.
stub_repos_sh 'printf "%s\n" FS-GG/FSxGG.SDD' "$healthy_kit"
expect_gate "gate: roster match is literal, not a regex" 0 'nothing to do' \
  bash "$FSYNC" --check --repo FS-GG/FS.GG.SDD "$RECV"

# --- the distributor TRACKS the registry (.github#338) ---
# The defect was a literal that could not notice a registry edit: `check-board` and `pnext-item` were
# registered (and digest-checked on every edit) yet distributed to nobody, and `--check` stayed green.
# Add a `kind: skill` row to a COPY of the registry and assert the distributor now demands ITS source.
# Against the old literal this passed silently — the new row was simply never looked at.
TRACK="$WORK/tracks-registry"; mkdir -p "$TRACK/scripts" "$TRACK/registry"
cp "$SYNC" "$TRACK/scripts/coordination-sync"
cp "$REPO_ROOT/scripts/repos.sh" "$TRACK/scripts/repos.sh"
cp "$REPO_ROOT/scripts/fsgg-coord" "$TRACK/scripts/fsgg-coord"
mkdir -p "$TRACK/.claude"; cp -r "$REPO_ROOT/.claude/skills" "$TRACK/.claude/skills"
cp "$REPO_ROOT/registry/repos.yml" "$TRACK/registry/repos.yml"
# `kit:` is the last top-level key and its rows are one-line flow mappings, so a row appends cleanly.
# The source deliberately does NOT exist: a distributor that reads the registry must name it and die.
#
# The row's id and its source directory DIFFER on purpose. `validate` permits that — it never asserts
# `source == .claude/skills/<id>` — so a distributor that rebuilds the path from the id would go
# looking for 'phantom-skill' and never read the declaration it claims to serve. Assert it names the
# SOURCE, and does not name the id.
printf '  - { id: phantom-skill, kind: skill, source: .claude/skills/phantom-dir, sha256: %064d }\n' 0 \
  >> "$TRACK/registry/repos.yml"
track_out="$(bash "$TRACK/scripts/coordination-sync" --check "$RECV" 2>&1)" || true
printf '%s' "$track_out" | grep -q 'phantom-dir' \
  && ok "registry: a new 'kind: skill' row reaches the distributor" \
  || bad "registry: new skill row ignored by the distributor" "$track_out"
printf '%s' "$track_out" | grep -q 'phantom-skill' \
  && bad "registry: source path rebuilt from the id, not read from the row" "$track_out" \
  || ok "registry: the skill's path is its declared 'source', not '.claude/skills/<id>'"

echo "coordination-sync fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coordination-sync fixture FAILED"; exit 1; }
echo "coordination-sync fixture — OK"
