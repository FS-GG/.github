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
# A red gate MUST say why. Drift in the LAST managed file is the case that catches a `set -e` trap: a
# trailing `[ x ] && echo` in the print loop makes the whole pipeline exit 1 when the last line is not
# `ok`, killing the script before it emits a single ::error::. The rc is 1 either way — identical to a
# correct drift verdict — so an exit-code assertion stays GREEN while the gate goes red saying NOTHING.
# That is this repo's own #266 shape (a check whose report is missing), one level down, inside the check.
LAST_DST="$(bash "$SYNC" --check "$RECV" | grep '^ok: ' | tail -1 | sed 's/^ok: //')"
[ -n "$LAST_DST" ] || { echo "::error::fixture: could not identify the last managed file."; exit 1; }
printf 'tampered\n' >> "$RECV/$LAST_DST"
drift_out="$(bash "$SYNC" --check "$RECV" 2>&1 || true)"
printf '%s' "$drift_out" | grep -q "::error::coordination-sync: DRIFT (differs): $LAST_DST" \
  && ok "check: drift in the LAST managed file still ANNOTATES it (not just a silent rc 1)" \
  || bad "check: red with no ::error:: — the gate reports nothing" "$drift_out"
printf '%s' "$drift_out" | grep -q 'kit is INCOHERENT' \
  && ok "check: ...and still names the fix" \
  || bad "check: red without the remediation line" "$drift_out"
bash "$SYNC" "$RECV" >/dev/null                       # re-sync back to coherent

rm -rf "$RECV/.agents/skills/${SKILLS[0]}"
expect_rc "check: missing skill '${SKILLS[0]}' fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null                       # re-sync back to coherent
expect_rc "check: re-synced receiver passes again (rc 0)" 0 bash "$SYNC" --check "$RECV"
rm -f "$RECV/scripts/fsgg-coord"
expect_rc "check: missing client fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null

# --- the exec bit: `apply` SETS it, so `--check` must ASSERT it (#506) ----------------------------
# `diff -q` reads CONTENT. A receiver whose client has lost its +x bit is byte-identical to canonical, so
# a mode-blind check pronounced the kit coherent while every worker in that repo got `permission denied`
# running the client the gate had just certified. Strip ONLY the bit: the bytes must stay canonical, or
# the assertion below passes on ordinary content drift and proves nothing about the mode.
chmod -x "$RECV/scripts/fsgg-coord"
diff -q "$REPO_ROOT/scripts/fsgg-coord" "$RECV/scripts/fsgg-coord" >/dev/null \
  && ok "exec: (precondition) the de-chmodded client is still BYTE-IDENTICAL to canonical" \
  || bad "exec: precondition" "the fixture changed the bytes — the assertions below would pass for the wrong reason"
noexec_out="$(bash "$SYNC" --check "$RECV" 2>&1 || true)"
expect_rc "exec: a byte-identical but NON-EXECUTABLE client FAILS (rc 1)" 1 bash "$SYNC" --check "$RECV"
# rc alone cannot tell this drift from any other — the trap this fixture already warns about twice.
printf '%s' "$noexec_out" | grep -q '::error::coordination-sync: DRIFT (not executable): scripts/fsgg-coord' \
  && ok "exec: ...and NAMES the file and the kind, not a bare rc 1" \
  || bad "exec: red without naming the mode drift" "$noexec_out"
# "Re-sync it" reads as a no-op to someone whose file already matches byte for byte. Name the bit.
printf '%s' "$noexec_out" | grep -q 'chmod +x' \
  && ok "exec: ...and tells the reader to chmod +x" \
  || bad "exec: red without a remediation the reader can act on" "$noexec_out"
bash "$SYNC" "$RECV" >/dev/null
[ -x "$RECV/scripts/fsgg-coord" ] \
  && ok "exec: re-running apply RESTORES the bit" || bad "exec: apply did not restore the mode"
expect_rc "exec: ...and the re-synced receiver passes again (rc 0)" 0 bash "$SYNC" --check "$RECV"

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
FAKE="$WORK/fakeroot"; mkdir -p "$FAKE/scripts/lib" "$FAKE/registry"
cp "$SYNC" "$FAKE/scripts/coordination-sync"
# EVERY lib, not a named one: coordination-sync sources these at LOAD, so a fake root missing one dies
# on line 1 and every assertion below it fails with a shell error instead of the verdict it is testing.
# Naming them individually rotted the moment lib/roots.sh was added (#525) — the same "a hand-maintained
# copy of a list that already exists" shape as #338/#334. Copy the directory; it cannot fall behind.
cp "$REPO_ROOT"/scripts/lib/*.sh "$FAKE/scripts/lib/"
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
TRACK="$WORK/tracks-registry"; mkdir -p "$TRACK/scripts/lib" "$TRACK/registry"
cp "$SYNC" "$TRACK/scripts/coordination-sync"
cp "$REPO_ROOT"/scripts/lib/*.sh "$TRACK/scripts/lib/"   # every lib it sources at load — see the fakeroot note
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

# --- a usage error is a misconfiguration (exit 2), never a drift verdict (exit 1) (#350) ---
# `--repo` with no value used to hit bash's `${2:?…}`, which exits 1 — the code this script reserves
# for "the kit has drifted". A caller with a typo'd or unset short-id was told the kit was INCOHERENT
# before the script read a single file. Assert both the absent and the empty-but-present forms, since
# `${2:?…}` fired on each. Nothing asserted the exit code of a usage error here, which is how the
# defect survived three passes over #266.
expect_rc "usage: --repo with no value exits 2 (misconfig), not 1 (drift)" 2 \
  bash "$SYNC" --check --repo
expect_rc "usage: --repo with an empty value exits 2, not 1" 2 \
  bash "$SYNC" --check --repo "" "$RECV"
expect_rc "usage: an unknown flag still exits 2" 2 bash "$SYNC" --bogus-flag
expect_rc "usage: --help still exits 0" 0 bash "$SYNC" --help
# --help must reach the header's Exit: line. A hardcoded `sed` range silently truncated it, hiding
# the very exit-code contract asserted above; a fixture that only checks --help's rc would not notice.
help_out="$(bash "$SYNC" --help)"
printf '%s' "$help_out" | grep -q '^Exit: .*2 = misconfiguration' \
  && ok "usage: --help documents the exit-code contract it is asserted against" \
  || bad "usage: --help truncates before the Exit: block" "$help_out"
printf '%s' "$help_out" | grep -q '^Env: ' \
  && ok "usage: --help documents AGENT_SKILL_ROOTS" \
  || bad "usage: --help truncates before the Env: block" "$help_out"
usage_out="$(bash "$SYNC" --check --repo 2>&1 || true)"
printf '%s' "$usage_out" | grep -q '^coordination-sync: ' \
  && ok "usage: the diagnostic carries the script's prefix, not bash's raw 'line N:'" \
  || bad "usage: raw bash diagnostic — will not annotate in Actions" "$usage_out"

# --- --base-ref ATTRIBUTES the drift instead of merely reporting it (#450) ------------------------
# The check answered "does <target> equal canonical?" and printed one red for every way the answer could
# be no. Three unrelated situations produce that red and only ONE is the branch author's to fix. Canonical
# is `.github@main` and it moves constantly, so the other two fire on PRs that never went near the kit:
# a worker read one as a repo defect and filed a long, evidenced issue about a kit resync that had merged
# 110 SECONDS EARLIER (FS.GG.Rendering#473), and a second worker lost an hour to the same signal.
#
# Build a REAL receiver git repo, because merge-base is the whole mechanism and a fake cannot exercise it:
#
#   B0 ──── B1(old kit) ──── B2(canonical kit)      <- main
#                   └──────── F1(no kit edit)       <- feature, cut BEFORE the sync landed
#
# `feature`'s working tree holds the OLD kit — it is drifted from canonical in the only sense the old
# check could see, and it is not the branch's doing.
G="$WORK/gitrecv"; mkdir -p "$G"
git -C "$G" init -q -b main
git -C "$G" config user.email fixture@fs-gg.invalid
git -C "$G" config user.name  "coordination-sync fixture"
gcommit() { git -C "$G" add -A && git -C "$G" commit -qm "$1"; }

echo "unrelated" > "$G/README.md"; gcommit "B0: repo exists"
bash "$SYNC" "$G" >/dev/null                                  # canonical kit...
printf '\n# stale byte from the previous kit\n' >> "$G/scripts/fsgg-coord"   # ...aged by one byte
gcommit "B1: the kit as it was BEFORE the sync"
B1="$(git -C "$G" rev-parse HEAD)"
git -C "$G" checkout -q -b feature
echo "my actual work" > "$G/src.txt"; gcommit "F1: a change that never touches the kit"
git -C "$G" checkout -q main
bash "$SYNC" "$G" >/dev/null; gcommit "B2: the kit sync lands on main"
git -C "$G" checkout -q feature                               # ...and the branch never saw it

# expect_out <name> <want-rc> <want-regex> <cmd...>  — the MESSAGE is the fix here, so assert on it.
expect_out() { local n="$1" want="$2" re="$3"; shift 3; local out rc=0; out="$("$@" 2>&1)" || rc=$?
  if [ "$rc" -eq "$want" ] && printf '%s' "$out" | grep -qE "$re"; then ok "$n"
  else bad "$n" "want rc=$want matching /$re/; got rc=$rc: $out"; fi; }

# (3) THE PHANTOM. Stale branch, main already in sync, PR touches no kit file -> GREEN, with an advisory
# that names the real situation. This is the exact shape that manufactured Rendering#473. Without
# --base-ref the very same tree is still a hard red — proving the relaxation is the ATTRIBUTION, not a
# weakening of the check.
expect_rc  "base-ref: (strict) a stale branch is still DRIFT without --base-ref (rc 1)" 1 \
  bash "$SYNC" --check "$G"
expect_out "base-ref: (3) stale branch + coherent main -> rc 0, no error" 0 \
  'coordination-sync: OK' bash "$SYNC" --check --base-ref main "$G"
expect_out "base-ref: (3) ...and says the branch merely PREDATES the sync" 0 \
  'predates a kit sync that is ALREADY on' bash "$SYNC" --check --base-ref main "$G"
out3="$(bash "$SYNC" --check --base-ref main "$G" 2>&1)"
printf '%s' "$out3" | grep -q '::error::' \
  && bad "base-ref: (3) must not emit an ERROR annotation on an innocent branch" "$out3" \
  || ok "base-ref: (3) the innocent branch gets no ::error:: annotation"

# (2) Main itself is BEHIND canonical (the propagate window). Still not the branch's fault: advise, do
# not red. B1 is main-as-it-was, so pointing --base-ref at it reproduces exactly that window.
expect_out "base-ref: (2) main behind canonical -> rc 0 (not the branch's drift)" 0 \
  'is BEHIND the canonical kit' bash "$SYNC" --check --base-ref "$B1" "$G"
expect_out "base-ref: (2) ...and names the arm that owes the fix" 0 \
  'coordination-propagate owes' bash "$SYNC" --check --base-ref "$B1" "$G"
out2="$(bash "$SYNC" --check --base-ref "$B1" "$G" 2>&1)"
printf '%s' "$out2" | grep -q '::error::' \
  && bad "base-ref: (2) a repo-level drift must not red an innocent PR" "$out2" \
  || ok "base-ref: (2) repo drift is a ::warning::, not an ::error::"
# ...but the receiver's own main run — strict, no --base-ref — is the verdict of record and stays RED.
# Without this, "advisory" would mean the drift is reported to nobody.
git -C "$G" stash -q -u 2>/dev/null || true
git -C "$G" checkout -q "$B1"
expect_rc "base-ref: (2) the repo's OWN main run is still a hard red (rc 1)" 1 bash "$SYNC" --check "$G"
git -C "$G" checkout -q feature

# (1) The branch DOES edit a vendored kit file, and the result is not canonical. That is real drift, it
# is the branch's, and it stays red — the relaxation above must not have opened this door.
printf '\n# hand-edited in the PR\n' >> "$G/scripts/fsgg-coord"; gcommit "F2: hand-edit the vendored kit"
expect_out "base-ref: (1) a branch that hand-edits the kit is STILL a hard red (rc 1)" 1 \
  'changes a vendored kit file' bash "$SYNC" --check --base-ref main "$G"
expect_out "base-ref: (1) ...and says where the kit is actually owned" 1 \
  'owned by FS-GG/.github' bash "$SYNC" --check --base-ref main "$G"

# (1b) The coordination-kit/sync PR itself. It touches kit files BY DESIGN and matches canonical, so it
# must be green — a check that reds the one PR that fixes the drift would deadlock the whole fabric.
git -C "$G" checkout -q -b kitsync "$B1"
bash "$SYNC" "$G" >/dev/null; gcommit "S1: the propagate arm's sync commit"
expect_out "base-ref: (1b) the sync PR touches the kit and MATCHES canonical -> rc 0" 0 \
  'this branch updates the kit, and the result matches canonical' bash "$SYNC" --check --base-ref "$B1" "$G"

# (1c) MIXED: the branch authors a kit change AND inherits a stale one. Attribution is per FILE, not
# all-or-nothing on "did it touch the kit at all" — a branch that FIXES one file must not be red for
# another it merely inherited. That coarser test would re-commit this issue's own misattribution one
# level in, and it is the exact bug the first draft of this fix shipped with.
#
# `mixed` is cut from a base stale in TWO kit files, and restores only the client.
git -C "$G" checkout -q main
printf '\n# stale\n' >> "$G/scripts/fsgg-coord"
printf '\n# stale\n' >> "$G/.agents/skills/${SKILLS[0]}/SKILL.md"
gcommit "M0: main falls behind canonical in two kit files"
M0="$(git -C "$G" rev-parse HEAD)"
git -C "$G" checkout -q -b mixed
cp "$REPO_ROOT/scripts/fsgg-coord" "$G/scripts/fsgg-coord"     # fix ONE of the two, by hand
gcommit "M1: restore the client to canonical; the skill stays stale (inherited)"
mixed_out="$(bash "$SYNC" --check --base-ref "$M0" "$G" 2>&1)"; mixed_rc=$?
[ "$mixed_rc" -eq 0 ] \
  && ok "base-ref: (1c) a branch that FIXES one kit file is not red for one it INHERITED (rc 0)" \
  || bad "base-ref: (1c) blamed for inherited drift" "$mixed_out"
printf '%s' "$mixed_out" | grep -q "::error::.*scripts/fsgg-coord" \
  && bad "base-ref: (1c) the file the branch FIXED is reported as its drift" "$mixed_out" \
  || ok "base-ref: (1c) the fixed file is not reported as drift"
printf '%s' "$mixed_out" | grep -q "::warning::.*${SKILLS[0]}" \
  && ok "base-ref: (1c) the INHERITED stale file is an advisory, attributed to the base" \
  || bad "base-ref: (1c) inherited drift went unreported" "$mixed_out"

# ...and the same branch, if it BREAKS a file it authored, is still red for THAT file only.
printf '\n# hand-mangled\n' >> "$G/scripts/fsgg-coord"; gcommit "M2: mangle the client it touched"
expect_out "base-ref: (1c) ...but drift it AUTHORED is still a hard red (rc 1)" 1 \
  '::error::coordination-sync: DRIFT \(differs\): scripts/fsgg-coord' \
  bash "$SYNC" --check --base-ref "$M0" "$G"

# Misconfiguration is never a drift verdict (#350). "I cannot attribute this" must fail CLOSED and loud,
# not silently fall back to a green — a gate that cannot tell whose drift it is has not cleared anyone.
# --- the exec RULE itself fails closed (#315's shape, one level up) -------------------------------
# The rule "must this dest be executable?" is read off the canonical SOURCE's own mode, which is what makes
# apply and check agree by construction. That trade has a failure mode: a .github checkout that has itself
# lost the client's +x bit would answer "no" everywhere, and BOTH arms would quietly stop caring — apply
# would distribute a dead client and check would certify it. A source we cannot trust is not a verdict.
EXROOT="$WORK/exroot"; mkdir -p "$EXROOT/scripts" "$EXROOT/registry"
cp -r "$REPO_ROOT/scripts/lib" "$EXROOT/scripts/lib"
cp "$SYNC" "$EXROOT/scripts/coordination-sync"
cp "$REPO_ROOT/scripts/repos.sh" "$EXROOT/scripts/repos.sh"
cp "$REPO_ROOT/registry/repos.yml" "$EXROOT/registry/repos.yml"
cp "$REPO_ROOT/scripts/fsgg-coord" "$EXROOT/scripts/fsgg-coord"
for src in "${SKILL_SRCS[@]}"; do
  mkdir -p "$EXROOT/$src"; cp "$REPO_ROOT/$src/SKILL.md" "$EXROOT/$src/SKILL.md"
done
chmod -x "$EXROOT/scripts/fsgg-coord"       # every kit source PRESENT; the client just isn't runnable
expect_gate "exec: a canonical client that is NOT executable fails closed (rc 2), not a green certificate" 2 \
  'refusing to distribute or certify' \
  bash "$EXROOT/scripts/coordination-sync" --check "$RECV"
mkdir -p "$WORK/wouldbe"        # a real target, or apply dies at "target not found" and never reaches the guard
expect_gate "exec: ...and apply refuses to distribute a dead client too" 2 \
  'refusing to distribute or certify' \
  bash "$EXROOT/scripts/coordination-sync" "$WORK/wouldbe"
[ -e "$WORK/wouldbe/scripts/fsgg-coord" ] \
  && bad "exec: apply wrote a non-executable client before dying" \
  || ok "exec: ...and wrote NOTHING — it refused before distributing, not after"

# --- the mode is read from the TREE under --base-ref, and rides the attribution (#450) ------------
# Two different code paths read a mode: the worktree (`test -x`, asserted above) and a <ref> (`ls-tree`,
# here). The attributed arm reads the BASE through the second one, so a <ref> read that cannot see a mode
# calls a base whose client is 100644 "coherent" — and then tells an innocent author their branch merely
# "predates a kit sync", which is both wrong and unactionable. Assert the verdict that distinguishes them.
M="$WORK/moderecv"; mkdir -p "$M"
git -C "$M" init -q -b main
git -C "$M" config user.email fixture@fs-gg.invalid
git -C "$M" config user.name  "coordination-sync fixture"
mcommit() { git -C "$M" add -A && git -C "$M" commit -qm "$1"; }
echo "unrelated" > "$M/README.md"
bash "$SYNC" "$M" >/dev/null; mcommit "main: canonical kit, client executable"

# (1) A branch that strips the bit AUTHORED it — `git diff` records a mode change, so attribution sees it.
git -C "$M" checkout -q -b stripmode
chmod -x "$M/scripts/fsgg-coord"; mcommit "strip the client's exec bit"
expect_out "exec: (base-ref) a branch that strips the bit OWNS the red (rc 1)" 1 \
  'DRIFT \(not executable\): scripts/fsgg-coord' bash "$SYNC" --check --base-ref main "$M"
expect_out "exec: (base-ref) ...and is told where the kit is owned" 1 \
  'owned by FS-GG/.github' bash "$SYNC" --check --base-ref main "$M"

# (2) An INHERITED mode drift is the REPO's, not the branch's. This is the assertion that actually proves
# the <ref> read sees modes: if it did not, the base would look coherent and this would report (3) instead.
git -C "$M" checkout -q main
chmod -x "$M/scripts/fsgg-coord"; mcommit "main: the bit is lost on main itself"
git -C "$M" checkout -q -b innocent
echo "my actual work" > "$M/src.txt"; mcommit "a change that never touches the kit"
expect_out "exec: (base-ref) an inherited mode drift is the REPO's — the base's mode is read from the tree" 0 \
  'is BEHIND the canonical kit' bash "$SYNC" --check --base-ref main "$M"
mode_out="$(bash "$SYNC" --check --base-ref main "$M" 2>&1)"
printf '%s' "$mode_out" | grep -q 'predates a kit sync' \
  && bad "exec: (base-ref) a mode-blind base read misdiagnosed inherited drift as a stale branch" "$mode_out" \
  || ok "exec: (base-ref) ...and is NOT misreported as a branch that merely predates a sync"
printf '%s' "$mode_out" | grep -q '::error::' \
  && bad "exec: (base-ref) an innocent branch was RED for drift it inherited" "$mode_out" \
  || ok "exec: (base-ref) ...and the innocent branch gets no ::error::"
# ...while the repo's own main run — strict, no --base-ref — stays the verdict of record, and stays red.
expect_rc "exec: (base-ref) the repo's OWN main run is still a hard red (rc 1)" 1 bash "$SYNC" --check "$M"

# The <ref> mode read must resolve the path the way the `:./` reads beside it do — RELATIVE TO <target>,
# which the gate's `target-path` input lets be a SUBDIRECTORY of the checkout. A root-relative pathspec
# would silently read a DIFFERENT file, so plant a decoy at the root with the same relative path and the
# wrong mode.
#
# The verdict has to DEPEND on the base's mode, or the assertion is vacuous — and the obvious version of
# this test is: a coherent receiver stays green either way, because the check returns before it ever looks
# at the base. The base's mode only decides anything when the branch has INHERITED drift, where a coherent
# base means "(3) you merely predate a sync" and a drifted one means "(2) the repo is BEHIND". So build (3)
# — a stale branch over a base that is in sync — and assert THAT verdict: a root-relative read finds the
# 100644 decoy, calls the base drifted, and flips the answer to (2).
S="$WORK/subdirrecv"; mkdir -p "$S/sub"
git -C "$S" init -q -b main
git -C "$S" config user.email fixture@fs-gg.invalid
git -C "$S" config user.name  "coordination-sync fixture"
scommit() { git -C "$S" add -A && git -C "$S" commit -qm "$1"; }
echo "unrelated" > "$S/README.md"
bash "$SYNC" "$S/sub" >/dev/null                                    # the receiver lives in a SUBDIRECTORY
printf '\n# stale byte from the previous kit\n' >> "$S/sub/scripts/fsgg-coord"
mkdir -p "$S/scripts"                                               # ...and a decoy sits at the ROOT,
cp "$REPO_ROOT/scripts/fsgg-coord" "$S/scripts/fsgg-coord"          # same relative path, wrong mode
chmod -x "$S/scripts/fsgg-coord"
scommit "B1: the subdir kit as it was BEFORE the sync; a non-executable decoy at the root"
git -C "$S" checkout -q -b feature
echo "my actual work" > "$S/src.txt"; scommit "F1: a change that never touches the kit"
git -C "$S" checkout -q main
bash "$SYNC" "$S/sub" >/dev/null; scommit "B2: the kit sync lands on main"
git -C "$S" checkout -q feature                                     # ...and the branch never saw it
expect_out "exec: (base-ref) the tree read resolves against <target>, not the repo root (subdir receiver)" 0 \
  'predates a kit sync' bash "$SYNC" --check --base-ref main "$S/sub"
sub_out="$(bash "$SYNC" --check --base-ref main "$S/sub" 2>&1)"
printf '%s' "$sub_out" | grep -q 'is BEHIND the canonical kit' \
  && bad "exec: (base-ref) the base's mode was read from the ROOT decoy, not from <target>" "$sub_out" \
  || ok "exec: (base-ref) ...so a coherent base is not called drifted on a decoy's mode"

expect_out "base-ref: a non-git target is a misconfig (rc 2), not a verdict" 2 \
  'needs <target> to be a git checkout' bash "$SYNC" --check --base-ref main "$RECV"
expect_out "base-ref: an unresolvable ref is a misconfig (rc 2), not a verdict" 2 \
  'is not resolvable' bash "$SYNC" --check --base-ref no/such/ref "$G"
expect_out "base-ref: --base-ref on a WRITE is a misconfig (rc 2)" 2 \
  'only meaningful with --check' bash "$SYNC" --base-ref main "$G"
expect_rc  "base-ref: with no value exits 2 (misconfig), not 1 (drift)" 2 \
  bash "$SYNC" --check --base-ref

# --- the receiver's .agent-skill-roots is the ONE source of truth for its root set (#525) ---------
# `skill-union-assert` has read a tree's checked-in `.agent-skill-roots` since #517. The distributor —
# the thing that MATERIALIZES those roots — did not: it hardcoded `${AGENT_SKILL_ROOTS:-.claude/skills
# .agents/skills}` at load time, BEFORE <target> was even parsed, so the receiver's own declaration was
# structurally unreachable. The two agreed only by coincidence of defaults.
#
# The failure that bought this fixture: a receiver ADDS a root. The writer never fills it, the gate then
# asserts it, and the receiver is told its tree is `[partitioned]` — blamed for the distributor's
# omission. The reverse is worse and quieter: a receiver REMOVES a root, the writer keeps writing it, the
# gate stops checking it, and it rots out of the union unwatched (#266/#292, the fail-open family).
DECL="$WORK/decl-receiver"; mkdir -p "$DECL"
printf '# a receiver that holds a THIRD root\n.claude/skills .agents/skills .gemini/skills  # trailing comment\n' \
  > "$DECL/.agent-skill-roots"
bash "$SYNC" "$DECL" >/dev/null
one_skill="${SKILLS[0]}"
[ -f "$DECL/.gemini/skills/$one_skill/SKILL.md" ] \
  && ok "roots: an ADDED root in the receiver's .agent-skill-roots IS materialized" \
  || bad "roots: declared .gemini/skills was not written" "the writer ignored the declaration the gate asserts"
# ...and the gate, resolving through the SAME helper, agrees the tree is coherent. This is the whole
# point of the hoist: writer and asserter cannot disagree about what the root set IS.
expect_rc "roots: the gate agrees the declared 3-root tree is coherent" 0 \
  bash "$REPO_ROOT/scripts/skill-union-assert.sh" --product "$DECL"

# A REMOVED root must not be written. Declaring one root means one root — not "the default, plus".
NARROW="$WORK/narrow-receiver"; mkdir -p "$NARROW"
printf '.claude/skills\n' > "$NARROW/.agent-skill-roots"
bash "$SYNC" "$NARROW" >/dev/null
[ -f "$NARROW/.claude/skills/$one_skill/SKILL.md" ] \
  && ok "roots: a NARROWED declaration still gets its declared root" || bad "roots: narrowed root missing"
[ ! -e "$NARROW/.agents/skills" ] \
  && ok "roots: a root the receiver did NOT declare is not written" \
  || bad "roots: wrote .agents/skills into a receiver that declared only .claude/skills" \
         "the declaration is the root set, not a supplement to the hardcoded default"

# Precedence: $AGENT_SKILL_ROOTS still overrides the declaration (CI's knob keeps working), and the
# default still applies to a receiver that declares nothing — the coincidence that held until now.
ENVR="$WORK/env-receiver"; mkdir -p "$ENVR"
printf '.gemini/skills\n' > "$ENVR/.agent-skill-roots"
AGENT_SKILL_ROOTS=".claude/skills" bash "$SYNC" "$ENVR" >/dev/null
[ -f "$ENVR/.claude/skills/$one_skill/SKILL.md" ] && [ ! -e "$ENVR/.gemini/skills" ] \
  && ok "roots: \$AGENT_SKILL_ROOTS still beats the receiver's declaration" \
  || bad "roots: env override no longer wins" "precedence must stay: env > .agent-skill-roots > default"

PLAIN="$WORK/plain-receiver"; mkdir -p "$PLAIN"
bash "$SYNC" "$PLAIN" >/dev/null
[ -f "$PLAIN/.claude/skills/$one_skill/SKILL.md" ] && [ -f "$PLAIN/.agents/skills/$one_skill/SKILL.md" ] \
  && ok "roots: a receiver declaring nothing still gets the kit lane's two (default unchanged)" \
  || bad "roots: default root set changed" "the kit lane's default is NOT ADR-0011's three"

# An empty/comment-only declaration is a MISCONFIGURATION (rc 2), never a silent fall-back to the
# default: a tree that checked the file in meant to say something, and substituting the default there
# would quietly hand it a root set it explicitly declined.
EMPTY="$WORK/empty-decl-receiver"; mkdir -p "$EMPTY"
printf '# all comments, no roots\n\n' > "$EMPTY/.agent-skill-roots"
expect_out "roots: an empty .agent-skill-roots is a misconfig (rc 2), not a fall-back to the default" 2 \
  'declares no roots' bash "$SYNC" "$EMPTY"

echo "coordination-sync fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coordination-sync fixture FAILED"; exit 1; }
echo "coordination-sync fixture — OK"
