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

# The root-selection diagnostic is the operator's explanation of the very constants that determine
# where kit bytes go.  Keep every caller's DEFAULT_LABEL beside DEFAULT_ROOTS, and lint the literal
# count instead of exercising a default run and hoping its prose happened to be inspected.  The four
# callers deliberately have different defaults (two runtime roots versus one materialized root), so
# each must describe its own constant rather than repeat an ADR-era count from another lane (#1879).
assert_default_root_label() { # <script>
  local script="$1" roots label count word
  roots="$(sed -n 's/^DEFAULT_ROOTS="\(.*\)".*/\1/p' "$REPO_ROOT/$script")"
  label="$(sed -n 's/^DEFAULT_LABEL="\(.*\)".*/\1/p' "$REPO_ROOT/$script")"
  # shellcheck disable=SC2086 # these are the space-separated root literals under test
  set -- $roots
  count=$#
  case "$count" in
    1) word=one ;;
    2) word=two ;;
    3) word=three ;;
    *) bad "root-label lint: $script has an unsupported root count" "DEFAULT_ROOTS='$roots'"; return ;;
  esac

  if [ -z "$label" ] || ! grep -Eq "(^|[^[:alpha:]])$word([^[:alpha:]]|$)" <<<"$label"; then
    bad "root-label lint: $script labels its $count root(s) truthfully" \
        "DEFAULT_ROOTS='$roots'; DEFAULT_LABEL='$label'"
  elif ! grep -F 'resolve_roots ' "$REPO_ROOT/$script" | grep -Fq '"$DEFAULT_LABEL"'; then
    bad "root-label lint: $script passes its adjacent DEFAULT_LABEL to resolve_roots" \
        "the call site must use the variable, not an inlined label"
  else
    ok "root-label lint: $script's label states its own $count root(s)"
  fi
}

for root_caller in \
  scripts/skill-view \
  scripts/skill-view-parity.sh \
  scripts/skill-union-assert.sh \
  scripts/coordination-sync; do
  assert_default_root_label "$root_caller"
done

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
IDENTITY_SKILL="cross-repo-coordination"
identity_skill_registered=false
for skill in "${SKILLS[@]}"; do
  if [ "$skill" = "$IDENTITY_SKILL" ]; then
    identity_skill_registered=true
    break
  fi
done
[ "$identity_skill_registered" = true ] \
  || { echo "::error::fixture: real identity subject $IDENTITY_SKILL is absent from the kit roster"; exit 1; }

# --- apply writes the full kit ---
bash "$SYNC" "$RECV" >/dev/null
[ -f "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client written"        || bad "apply: client"
[ -x "$RECV/scripts/fsgg-coord" ]                                  && ok "apply: client is executable" || bad "apply: client exec bit"
diff -q "$REPO_ROOT/scripts/fsgg-coord" "$RECV/scripts/fsgg-coord" >/dev/null \
  && ok "apply: client bytes match canonical" || bad "apply: client bytes"

# EVERY registered skill, in EVERY MATERIALIZED root. Register a skill in repos.yml and forget the
# distributor and these go red — where before, `--check` passed on a receiver that lacked it entirely.
#
# THE ROOT SET NARROWED TO ONE — ADR-0067 §9 stage 2 (.github#1676). This loop used to read
# `for root in .claude/skills .agents/skills`, asserting the distributor wrote BOTH. `.agents/skills`
# is now a generated VIEW root: its content is produced at checkout by `scripts/skill-view generate`
# and is never transported, so a distributor that writes it is committing the second copy the view
# exists to remove — the same defect FS.GG.SDD#770 and FS.GG.Governance#338 each closed in their own
# tool. The RUNTIME contract is unchanged: it is the union of the materialized and view roots, and
# that union is still ADR-0065's two. Only who PRODUCES the second one changed.
materialized_root='.claude/skills'
view_root='.agents/skills'
for i in "${!SKILLS[@]}"; do
  s="${SKILLS[$i]}"; src="${SKILL_SRCS[$i]}"
  # ONE materialized root, so this is a straight assertion rather than a loop over one item — the
  # loop that used to be here read `for root in .claude/skills .agents/skills` and shellcheck SC2043
  # correctly flags its one-element successor as a loop that can only run once. The narrowing is
  # stated in the comment above; the code should not pretend to iterate.
  [ -f "$RECV/$materialized_root/$s/SKILL.md" ] && ok "apply: $s in $materialized_root" \
    || bad "apply: $s in $materialized_root" "declared 'kind: skill' in repos.yml, not distributed"
  # Bytes come from the registry's declared `source`, not from a path rebuilt out of the id.
  diff -q "$REPO_ROOT/$src/SKILL.md" "$RECV/.claude/skills/$s/SKILL.md" >/dev/null \
    && ok "apply: $s bytes match canonical" || bad "apply: $s bytes"

  # AND THE VIEW ROOT IS NOT WRITTEN — the constructed half of the narrowing above.
  #
  # Dropping `.agents/skills` from the loop on its own would REMOVE coverage rather than change what
  # is covered: the suite would then be silent about the view root, and a distributor that started
  # writing it again would go unnoticed. This asserts the new contract positively — stage 2 is that
  # the distributor stops producing that root, so the absence IS the property. It is red against the
  # pre-stage-2 distributor, which wrote every skill here (measured: 4 legs, one per registered kit
  # skill).
  [ -e "$RECV/$view_root/$s" ] \
    && bad "apply: $s must NOT be distributed into the view root $view_root" \
           "a view root is generated by scripts/skill-view, never transported (ADR-0067 §9 stage 2)" \
    || ok "apply: $s is NOT written into the view root $view_root"
done

# The kit's `kind: config` files (the engine manifest, #1077) land at their declared DEST — not their
# source path — bytes match, and being DATA rather than the client they are NOT made executable. Read
# them the way apply does: from the registry, by (source, dest), never a path rebuilt from the id.
mapfile -t CFG_SRC < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --kind config \
                         --registry "$REPO_ROOT/registry/repos.yml")
mapfile -t CFG_DST < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field dest --kind config \
                         --registry "$REPO_ROOT/registry/repos.yml")
for i in "${!CFG_SRC[@]}"; do
  csrc="${CFG_SRC[$i]}"; cdst="${CFG_DST[$i]}"
  [ -n "$csrc" ] || continue
  [ -f "$RECV/$cdst" ] && ok "apply: config $cdst written (to its dest, not its source path)" \
    || bad "apply: config $cdst" "declared 'kind: config' in repos.yml, not distributed"
  diff -q "$REPO_ROOT/$csrc" "$RECV/$cdst" >/dev/null \
    && ok "apply: config $cdst bytes match canonical" || bad "apply: config $cdst bytes"
  [ ! -x "$RECV/$cdst" ] && ok "apply: config $cdst is data, left non-executable" \
    || bad "apply: config $cdst exec bit" "a config file must not be marked executable"
done

# --- check passes when coherent ---
expect_rc "check: coherent receiver passes (rc 0)" 0 bash "$SYNC" --check "$RECV"

# Directory transport is a closed set and mode parity is bidirectional: unexpected +x and an
# undeclared leftover file are drift, and apply repairs both.
chmod a+x "$RECV/.claude/skills/${SKILLS[0]}/SKILL.md"
expect_rc "check: unexpected executable bit on skill data fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null
[ ! -x "$RECV/.claude/skills/${SKILLS[0]}/SKILL.md" ] \
  && ok "apply: normalizes an unexpected executable bit" || bad "apply: did not normalize skill mode"
printf 'stale\n' > "$RECV/.claude/skills/${SKILLS[0]}/undeclared.txt"
expect_rc "check: extra undeclared skill file fails (rc 1)" 1 bash "$SYNC" --check "$RECV"
bash "$SYNC" "$RECV" >/dev/null
[ ! -e "$RECV/.claude/skills/${SKILLS[0]}/undeclared.txt" ] \
  && ok "apply: removes an extra undeclared skill file" || bad "apply: did not close the skill file set"

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

rm -rf "$RECV/.claude/skills/${SKILLS[0]}"
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
# EVERY `kind: client` source, derived from the registry rather than named here (.github#1696): the
# distributor's source-existence guard runs before the read this leg is about, so a client this fake
# root omits would die on the missing file and the phantom-dir assertion below could never be reached.
# Deriving it also means a future client row needs no edit here — the same rule the assertion tests.
while IFS= read -r _c; do
  [ -n "$_c" ] || continue
  mkdir -p "$TRACK/$(dirname "$_c")"; cp "$REPO_ROOT/$_c" "$TRACK/$_c"
done < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --kind client --registry "$REPO_ROOT/registry/repos.yml")
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
# ...and it documents the INCONCLUSIVE code too (#1584). Asserted on the SAME line rather than anywhere
# in the header, because a code that wraps onto a continuation line is a code a caller reading the
# contract off `Exit:` does not see — the truncation defect above, one field over.
printf '%s' "$help_out" | grep -q '^Exit: .*3 = INCONCLUSIVE' \
  && ok "usage: --help documents the INCONCLUSIVE exit code on the Exit: line (#1584)" \
  || bad "usage: --help does not carry '3 = INCONCLUSIVE' on the Exit: line" "$help_out"
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
expect_out "base-ref: (2) ...and names the delivery path that clears it" 0 \
  'kit-materialize' bash "$SYNC" --check --base-ref "$B1" "$G"
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
printf '\n# stale\n' >> "$G/.claude/skills/${SKILLS[0]}/SKILL.md"
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
mapfile -t CLIENT_SRCS < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --kind client \
                            --registry "$REPO_ROOT/registry/repos.yml")
for src in "${CLIENT_SRCS[@]}"; do
  [ -n "$src" ] || continue
  mkdir -p "$EXROOT/$(dirname "$src")"; cp "$REPO_ROOT/$src" "$EXROOT/$src"
done
for src in "${SKILL_SRCS[@]}"; do
  mkdir -p "$EXROOT/$src"; cp "$REPO_ROOT/$src/SKILL.md" "$EXROOT/$src/SKILL.md"
done
# The kit's `kind: config` sources (e.g. the engine manifest, #1077) must be present too, or the
# source-existence guard dies at the missing manifest BEFORE it can reach the exec guard this leg is
# about — "every kit source PRESENT" below has to include them.
mapfile -t CONFIG_SRCS < <(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --kind config \
                            --registry "$REPO_ROOT/registry/repos.yml")
for src in "${CONFIG_SRCS[@]}"; do
  [ -n "$src" ] || continue
  mkdir -p "$EXROOT/$(dirname "$src")"; cp "$REPO_ROOT/$src" "$EXROOT/$src"
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
#
# THE FIXTURE DECLARES A NON-DEFAULT ROOT ON PURPOSE (ADR-0067 §9 stage 2, .github#1676). It used to
# declare `.claude/skills` and assert `.agents/skills` was absent — which the stage-2 default now
# gives you FOR FREE, so the leg would pass on a build that ignored the declaration entirely. That is
# protection by position rather than construction (.github#1849). Declaring the OTHER root instead
# makes the assertion depend on the declaration being read in both directions.
NARROW="$WORK/narrow-receiver"; mkdir -p "$NARROW"
printf '.agents/skills\n' > "$NARROW/.agent-skill-roots"
bash "$SYNC" "$NARROW" >/dev/null
[ -f "$NARROW/.agents/skills/$one_skill/SKILL.md" ] \
  && ok "roots: a NARROWED declaration still gets its declared root" || bad "roots: narrowed root missing"
[ ! -e "$NARROW/.claude/skills" ] \
  && ok "roots: the DEFAULT root is not written when the receiver declared another" \
  || bad "roots: wrote .claude/skills into a receiver that declared only .agents/skills" \
         "the declaration is the root set, not a supplement to the hardcoded default"

# Precedence: $AGENT_SKILL_ROOTS still overrides the declaration (CI's knob keeps working), and the
# default still applies to a receiver that declares nothing.
ENVR="$WORK/env-receiver"; mkdir -p "$ENVR"
printf '.gemini/skills\n' > "$ENVR/.agent-skill-roots"
AGENT_SKILL_ROOTS=".claude/skills" bash "$SYNC" "$ENVR" >/dev/null
[ -f "$ENVR/.claude/skills/$one_skill/SKILL.md" ] && [ ! -e "$ENVR/.gemini/skills" ] \
  && ok "roots: \$AGENT_SKILL_ROOTS still beats the receiver's declaration" \
  || bad "roots: env override no longer wins" "precedence must stay: env > .agent-skill-roots > default"

# THE LEG STAGE 2 CHANGES, and the one that pins the new default (ADR-0067 §9 stage 2, .github#1676).
# A receiver declaring nothing used to get BOTH of ADR-0065's roots MATERIALIZED. It now gets one:
# `.agents/skills` is a generated VIEW root, produced at checkout by `scripts/skill-view generate`,
# never transported. The RUNTIME contract is unchanged — it is the union of the materialized and view
# roots, still ADR-0065's two — but the distributor produces only the first, and the retired
# `.codex/skills` is still written by nobody.
PLAIN="$WORK/plain-receiver"; mkdir -p "$PLAIN"
bash "$SYNC" "$PLAIN" >/dev/null
[ -f "$PLAIN/.claude/skills/$one_skill/SKILL.md" ] \
  && [ ! -e "$PLAIN/.agents/skills" ] \
  && [ ! -e "$PLAIN/.codex/skills" ] \
  && ok "roots: a receiver declaring nothing gets the ONE materialized root — not the view, not the retired one" \
  || bad "roots: default root set is wrong" "want .claude/skills materialized, .agents/skills NOT written (it is a generated view), no .codex"

# An empty/comment-only declaration is a MISCONFIGURATION (rc 2), never a silent fall-back to the
# default: a tree that checked the file in meant to say something, and substituting the default there
# would quietly hand it a root set it explicitly declined.
EMPTY="$WORK/empty-decl-receiver"; mkdir -p "$EMPTY"
printf '# all comments, no roots\n\n' > "$EMPTY/.agent-skill-roots"
expect_out "roots: an empty .agent-skill-roots is a misconfig (rc 2), not a fall-back to the default" 2 \
  'declares no roots' bash "$SYNC" "$EMPTY"

# --- PIN-RELATIVE VERIFICATION (--against-pin, #1584) ---------------------------------------------
#
# The gate's comparand is no longer THIS CHECKOUT at check time — it is the FS.GG.Kit package version
# the receiver itself pins, restored from the feed and compared against the sha256s that package's own
# kit-manifest.tsv ships. These assertions drive the REAL fetch/unzip/verify path: a genuine .nupkg is
# built here by the producer's own stage-kit.sh and served over a `file://` flat container, so only the
# base URL differs from production. Nothing is mocked, and there is no network.
echo "--- pin-relative (#1584) ---"

PINW="$WORK/pin"; mkdir -p "$PINW"
STAGE="$PINW/stage"
bash "$REPO_ROOT/src/FS.GG.Kit/stage-kit.sh" "$STAGE" >/dev/null 2>&1 \
  || { echo "::error::fixture: stage-kit.sh could not stage the kit — nothing to build a package from."; exit 1; }
[ -s "$STAGE/kit-manifest.tsv" ] \
  || { echo "::error::fixture: staged kit-manifest.tsv is empty."; exit 1; }

FEED="$PINW/feed"
# Pack with python3's zipfile rather than `zip(1)`: --against-pin already REQUIRES python3, so the
# fixture adds no dependency the thing under test does not have, and it runs where `zip` is absent.
pack_kit() {   # pack_kit <version> [<staging dir>]
  local version="$1" stage="${2:-$STAGE}" root="$PINW/pack-$1"
  rm -rf "$root"; mkdir -p "$root/kit" "$root/build"
  cp -r "$stage/." "$root/kit/"
  # build/ is packed from the SAME sources FS.GG.Kit.csproj packs, and it is not decoration: the pin
  # check reads its skill-root default out of build/FS.GG.Kit.props rather than out of this checkout.
  # A fixture package missing it would be testing a package shape that is never published.
  cp "$REPO_ROOT/src/FS.GG.Kit/build/FS.GG.Kit.props"   "$root/build/"
  cp "$REPO_ROOT/src/FS.GG.Kit/build/FS.GG.Kit.targets" "$root/build/"
  mkdir -p "$FEED/fs.gg.kit/$version"
  python3 - "$root" "$FEED/fs.gg.kit/$version/fs.gg.kit.$version.nupkg" <<'PY'
import os, sys, zipfile
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for dirpath, _dirs, files in os.walk(src):
        for name in files:
            full = os.path.join(dirpath, name)
            z.write(full, os.path.relpath(full, src).replace(os.sep, "/"))
PY
}
pack_kit 0.9.0

# The receiver: apply mode writes exactly the destinations the manifest names, which is what makes this
# a real end-to-end check rather than a restatement — the writer and the pin verifier are independent.
PRECV="$PINW/receiver"; mkdir -p "$PRECV"
bash "$SYNC" "$PRECV" >/dev/null

# `Version=` inline on the PackageReference — the no-CPM shape (FS.GG.Templates' today).
mkdir -p "$PRECV/.config/kit"
write_proj() {   # write_proj <attrs>
  cat > "$PRECV/.config/kit/FS.GG.Kit.receiver.proj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="FS.GG.Kit" $1 /></ItemGroup>
</Project>
EOF
}
write_proj 'Version="0.9.0"'

pin_env() { env FSGG_NUGET_ORG_BASE="file://$FEED" FSGG_KIT_FETCH_TRIES=1 FSGG_KIT_FETCH_BACKOFF_S=0 "$@"; }
pin_check() { pin_env bash "$SYNC" --check --against-pin "$@"; }

expect_rc "pin: a tree matching its pin is coherent (rc 0)" 0 pin_check "$PRECV"

# The identity projection is over the exact same restored package ledger, but names one skill and
# every materialized file/digest for agent consumption. Its inversion changes only the receiver.
identity_out="$(pin_check --identity "$IDENTITY_SKILL" "$PRECV")"
printf '%s' "$identity_out" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["verdict"]=="coherent" and d["artifacts"] and d["authority"]["version"]=="0.9.0"' \
  && ok "pin identity: coherent JSON binds package version and materialized artifacts" \
  || bad "pin identity: coherent projection" "$identity_out"
printf '\nidentity divergence\n' >> "$PRECV/.claude/skills/$IDENTITY_SKILL/SKILL.md"
expect_out "pin identity: one-line materialized divergence is RED and named (rc 1)" 1 \
  '"verdict": "drift"' pin_check --identity "$IDENTITY_SKILL" "$PRECV"
bash "$SYNC" "$PRECV" >/dev/null
identity_out="$(pin_check --identity "$IDENTITY_SKILL" "$PRECV")"
printf '%s' "$identity_out" | python3 -c 'import json,sys; assert json.load(sys.stdin)["verdict"]=="coherent"' \
  && ok "pin identity: re-materialization restores coherent identity" \
  || bad "pin identity: restored projection" "$identity_out"

# CRITERION 3 — GREEN REGARDLESS OF HUB STATE, proved directly.
#
# The receiver is put in EXACTLY the FS.GG.Audio position: pinned to an older kit, with that older kit
# faithfully materialized. Canonical (this checkout) has moved on. The strict, hub-relative arm MUST go
# red on that tree — that IS the #1584 defect, and asserting it here is what stops this from being a
# proof that the pin arm simply checks nothing. The pin arm MUST call the same tree coherent.
#
# The older kit is a real package built from a perturbed staging dir with its manifest rehashed, so the
# receiver is verified against a genuinely different comparand — and NOTHING in the repo checkout is
# mutated to arrange it, which a fixture that appended to a canonical source could not promise if it
# died between the write and the restore.
perturb_stage() {   # perturb_stage <staging dir> <package-relative path> — edit a file AND its manifest row
  python3 - "$1" "$2" <<'PY'
import hashlib, sys
stage, target = sys.argv[1], sys.argv[2]
path = f"{stage}/{target}"
with open(path, "ab") as fh:
    fh.write(b"\n# an older kit, faithfully materialized\n")
digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
manifest = f"{stage}/kit-manifest.tsv"
rows = []
for line in open(manifest, encoding="utf-8").read().splitlines():
    fields = line.split("\t")
    if len(fields) >= 4 and fields[1] == target:
        fields[3] = digest
    rows.append("\t".join(fields))
open(manifest, "w", encoding="utf-8").write("\n".join(rows) + "\n")
PY
}
OLDSTAGE="$PINW/stage-old"
cp -r "$STAGE" "$OLDSTAGE"
perturb_stage "$OLDSTAGE" client/fsgg-coord
pack_kit 0.8.0 "$OLDSTAGE"
cp "$OLDSTAGE/client/fsgg-coord" "$PRECV/scripts/fsgg-coord"; chmod a+x "$PRECV/scripts/fsgg-coord"
write_proj 'Version="0.8.0"'
expect_rc "pin: the STRICT hub-relative arm reds a receiver pinned behind canonical (rc 1) — the #1584 defect itself" 1 \
  bash "$SYNC" --check "$PRECV"
expect_rc "pin: ...and the SAME tree is coherent against its OWN pin (rc 0) — criterion 3" 0 \
  pin_check "$PRECV"
# ...and it is green because it VERIFIED, not because it skipped: the file that differs from canonical
# is the very one the pin arm reports ok.
expect_out "pin: ...and it is green having actually verified the file that differs from canonical" 0 \
  '^ok: scripts/fsgg-coord$' pin_check "$PRECV"
bash "$SYNC" "$PRECV" >/dev/null            # back to canonical == 0.9.0
write_proj 'Version="0.9.0"'

# CRITERION 4 — a tree that diverges from its pin is RED, and NAMES the file.
CLIENT_DEST="scripts/fsgg-coord"
cp "$PRECV/$CLIENT_DEST" "$PINW/client.orig"
printf '\n# tampered\n' >> "$PRECV/$CLIENT_DEST"
expect_out "pin: a file perturbed away from the pin is drift (rc 1)" 1 \
  "DRIFT \(differs\): $CLIENT_DEST" pin_check "$PRECV"
expect_out "pin: ...and the red says the hub cannot have caused it" 1 \
  'THIS TREE ONLY' pin_check "$PRECV"
cp "$PINW/client.orig" "$PRECV/$CLIENT_DEST"
expect_rc "pin: restoring the file clears the drift (rc 0)" 0 pin_check "$PRECV"

# The mode is part of the pin, exactly as it is part of the materialize (#506's shape, one comparand
# over): a client that is byte-identical but has lost +x is a kit no worker can run.
chmod a-x "$PRECV/$CLIENT_DEST"
expect_out "pin: a client that lost its exec bit is drift, and the red NAMES the bit (rc 1)" 1 \
  'LOST its executable bit' pin_check "$PRECV"
chmod a+x "$PRECV/$CLIENT_DEST"

# A skill directory is a CLOSED set on this side too — build/FS.GG.Kit.targets deletes undeclared files
# on every materialize, so a leftover from an older kit IS a tree that does not match its pin.
printf 'stale\n' > "$PRECV/.claude/skills/${SKILLS[0]}/leftover.txt"
expect_out "pin: an undeclared file in a managed skill dir is drift (rc 1)" 1 \
  'DRIFT \(extra\)' pin_check "$PRECV"
rm -f "$PRECV/.claude/skills/${SKILLS[0]}/leftover.txt"

# A missing file is drift, not a silent skip.
rm -f "$PRECV/.claude/skills/${SKILLS[0]}/SKILL.md"
expect_out "pin: a missing materialized file is drift (rc 1)" 1 \
  "DRIFT \(missing\): [.]claude/skills/${SKILLS[0]}/SKILL[.]md" pin_check "$PRECV"
bash "$SYNC" "$PRECV" >/dev/null

# CRITERION 2 — THE PIN LOCATION IS DERIVED. Each of the three shapes the org runs today is proved
# against the SAME receiver tree, so what changes between these cases is only where the version literal
# lives. None of the three is named in the resolver: they fall out of following MSBuild's own
# resolution from the receiver project.
expect_out "pin shape 1/3: inline Version on the PackageReference" 0 \
  'pin = 0[.]9[.]0 \(from [.]config/kit/FS[.]GG[.]Kit[.]receiver[.]proj' pin_check "$PRECV"

# Shape 2: CPM through Directory.Packages.props, which is where the hand-authored-build-config
# receivers (net, audio) pin.
write_proj ''
cat > "$PRECV/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageVersion Include="FS.GG.Kit" Version="0.9.0" /></ItemGroup>
</Project>
EOF
expect_out "pin shape 2/3: CPM via Directory.Packages.props (a versionless PackageReference)" 0 \
  'pin = 0[.]9[.]0 \(from Directory[.]Packages[.]props' pin_check "$PRECV"

# Shape 3: CPM through Directory.Packages.LOCAL.props — reached ONLY by following the canonical
# Directory.Packages.props's own <Import>. This is the case a filename list gets wrong, and the one
# that proves the resolver is following MSBuild rather than guessing: the literal is in a file the
# resolver is never told about.
cat > "$PRECV/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <Import Project="Directory.Packages.local.props" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Packages.local.props')" />
</Project>
EOF
cat > "$PRECV/Directory.Packages.local.props" <<'EOF'
<Project>
  <ItemGroup><PackageVersion Include="FS.GG.Kit" Version="0.9.0" /></ItemGroup>
</Project>
EOF
expect_out "pin shape 3/3: CPM via Directory.Packages.local.props, reached through the canonical file's own Import" 0 \
  'pin = 0[.]9[.]0 \(from Directory[.]Packages[.]local[.]props' pin_check "$PRECV"

# A VersionOverride BEATS the central PackageVersion, so grading the central one would verify a version
# the receiver does not restore — this sweep's own failure mode, one attribute over.
write_proj 'VersionOverride="0.9.0"'
cat > "$PRECV/Directory.Packages.local.props" <<'EOF'
<Project>
  <ItemGroup><PackageVersion Include="FS.GG.Kit" Version="0.6.0" /></ItemGroup>
</Project>
EOF
expect_out "pin: VersionOverride WINS over the central PackageVersion, rather than contradicting it" 0 \
  'pin = 0[.]9[.]0 \(from [.]config/kit/FS[.]GG[.]Kit[.]receiver[.]proj -> 0[.]9[.]0 \(VersionOverride\)' pin_check "$PRECV"

# Two CPM literals that DISAGREE with no override to settle them. The receiver project stays
# VERSIONLESS on purpose: an inline Version short-circuits the walk-up (correctly — MSBuild would
# restore it), so writing one here would have produced a single unambiguous pin and asserted nothing.
# The ambiguity that can really happen is two reachable central declarations, which is exactly what the
# 0.6.0 left over from the override case above now becomes once nothing outranks it.
write_proj ''
cat > "$PRECV/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageVersion Include="FS.GG.Kit" Version="0.9.0" /></ItemGroup>
  <Import Project="Directory.Packages.local.props" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Packages.local.props')" />
</Project>
EOF
expect_out "pin: two DISAGREEING central pins are INCONCLUSIVE (rc 3), never a pass" 3 \
  'pinned to more than one version' pin_check "$PRECV"
# ...and the refusal NAMES both files, or the reader cannot act on it.
expect_out "pin: ...and the refusal names both declaring files" 3 \
  'Directory[.]Packages[.]props -> 0[.]9[.]0.*Directory[.]Packages[.]local[.]props -> 0[.]6[.]0' \
  pin_check "$PRECV"

# --- CRITERION 5: "I could not check" is neither green nor a wiring finding -----------------------
# Every case below must exit 3 — its own code, distinct from 0 (coherent) and 1 (drifted).
rm -f "$PRECV/Directory.Packages.props" "$PRECV/Directory.Packages.local.props"
write_proj 'Version="0.9.0"'

# The pinned version is not on the feed. DETERMINISTIC, so it is reported PERMANENT and not retried.
write_proj 'Version="9.9.9"'
expect_out "pin: a pin the feed does not serve is INCONCLUSIVE/PERMANENT (rc 3), not green" 3 \
  'INCONCLUSIVE \(PERMANENT\)' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# The feed itself is unreachable. RETRYABLE — an outage must not read as a permanent no-verdict, which
# is the distinction #1540 already had to fix in the sibling sweep.
expect_out "pin: an unreachable feed is INCONCLUSIVE/RETRYABLE (rc 3), not green and not drift" 3 \
  'INCONCLUSIVE \(RETRYABLE\)' \
  env FSGG_NUGET_ORG_BASE="http://127.0.0.1:1/nope" FSGG_KIT_FETCH_TRIES=1 FSGG_KIT_FETCH_BACKOFF_S=0 \
      bash "$SYNC" --check --against-pin "$PRECV"

# No receiver project at all: unresolvable, so nothing was verified.
mv "$PRECV/.config/kit/FS.GG.Kit.receiver.proj" "$PINW/proj.parked"
expect_out "pin: no receiver project is INCONCLUSIVE (rc 3), not 'nothing to check'" 3 \
  'does not exist in this tree' pin_check "$PRECV"
mv "$PINW/proj.parked" "$PRECV/.config/kit/FS.GG.Kit.receiver.proj"

# A versionless PackageReference with no reachable Directory.Packages.props is the CPM shape with the
# pin missing. Unresolvable is not unpinned, and it is not current.
write_proj ''
expect_out "pin: a CPM shape with no reachable PackageVersion is INCONCLUSIVE (rc 3)" 3 \
  'The pin is unresolvable' pin_check "$PRECV"

# Unparsable XML. The one shape most likely to be mistaken for "no pin here, move on".
printf 'not xml at all <<<\n' > "$PRECV/.config/kit/FS.GG.Kit.receiver.proj"
expect_out "pin: an unparsable receiver project is INCONCLUSIVE (rc 3), never a pass" 3 \
  'Unparsable is not unpinned' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# SELF-ATTESTATION HAS A FLOOR (#1584 risk 3). The manifest ships inside the package it describes, so a
# tree-vs-manifest compare cannot see a package that disagrees with CANONICAL — that is
# check-kit-published-coherence.py's question. What it CAN see is a package that disagrees with ITSELF,
# and it must, or a corrupt package would grade a healthy tree as drifted and blame the wrong repo.
BADSTAGE="$PINW/stage-bad"
cp -r "$STAGE" "$BADSTAGE"
printf 'payload the manifest does not describe\n' >> "$BADSTAGE/client/fsgg-coord"
pack_kit 0.9.1 "$BADSTAGE"
write_proj 'Version="0.9.1"'
expect_out "pin: a package whose payload disagrees with its OWN manifest is INCONCLUSIVE (rc 3), not the receiver's drift" 3 \
  'PRODUCER defect' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# An empty manifest must not verify a tree vacuously — the floor build/FS.GG.Kit.targets holds on the
# materialize side, held again here (#266: "I checked nothing" is not "it is fine").
EMPTYSTAGE="$PINW/stage-empty"
mkdir -p "$EMPTYSTAGE"; : > "$EMPTYSTAGE/kit-manifest.tsv"
pack_kit 0.9.2 "$EMPTYSTAGE"
write_proj 'Version="0.9.2"'
expect_out "pin: an EMPTY manifest is INCONCLUSIVE (rc 3), never a vacuous pass" 3 \
  'names no files' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# --- CRITERION 6: the PR-arm asymmetry is DROPPED, and refused rather than silently ignored -------
# --base-ref exists to excuse a branch for drift a moving canonical caused. A pin-relative comparand is
# read from the branch's own tree, so there is no inherited drift to attribute — and a caller combining
# the flags has misunderstood the change rather than requested a mode.
expect_out "pin: --base-ref with --against-pin is a misconfig (rc 2), not a silently ignored flag" 2 \
  'mutually exclusive' bash "$SYNC" --check --against-pin --base-ref main "$PRECV"
expect_out "pin: --against-pin on a WRITE is a misconfig (rc 2)" 2 \
  'only meaningful with --check' bash "$SYNC" --against-pin "$PRECV"

# build-config is OPT-IN on the materialize side (FsggKitMaterializeBuildConfig, default false), so it
# must be opt-in here: verifying it unconditionally would red every receiver that correctly does not
# carry it. The default run above passed with no Directory.Build.props in the tree at all.
expect_out "pin: --include-build-config makes an absent build-config file drift (rc 1)" 1 \
  'DRIFT \(missing\): Directory[.]Build[.]props' \
  env FSGG_NUGET_ORG_BASE="file://$FEED" FSGG_KIT_FETCH_TRIES=1 FSGG_KIT_FETCH_BACKOFF_S=0 \
      bash "$SYNC" --check --against-pin --include-build-config "$PRECV"

# --- THE SKILL ROOTS COME FROM THE PIN, NOT FROM THIS CHECKOUT -----------------------------------
# WHERE a skill materializes is as much a part of the verdict as WHAT it contains, so if the pin check
# read its root set from `coordination-sync`'s own DEFAULT_ROOTS, a hub edit to that constant would red
# every receiver over files "missing" from a root they never adopted — #1584's defect arriving through
# the program instead of the data. The roots must therefore be pinned or receiver-owned.
expect_out "pin roots: default to the PINNED PACKAGE's build/FS.GG.Kit.props, not this checkout" 0 \
  "skill roots = .* [(]from the pinned package's build/FS[.]GG[.]Kit[.]props default[)]" pin_check "$PRECV"

# A package that cannot answer the question is INCONCLUSIVE — the hub's default is NOT substituted,
# which is the whole point. Built by dropping build/FS.GG.Kit.props from an otherwise valid package.
NOPROPS="$PINW/pack-noprops"
rm -rf "$NOPROPS"; mkdir -p "$NOPROPS/kit"; cp -r "$STAGE/." "$NOPROPS/kit/"
mkdir -p "$FEED/fs.gg.kit/0.9.3"
python3 - "$NOPROPS" "$FEED/fs.gg.kit/0.9.3/fs.gg.kit.0.9.3.nupkg" <<'PY'
import os, sys, zipfile
src, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for dirpath, _dirs, files in os.walk(src):
        for name in files:
            full = os.path.join(dirpath, name)
            z.write(full, os.path.relpath(full, src).replace(os.sep, "/"))
PY
write_proj 'Version="0.9.3"'
expect_out "pin roots: a package with no build/FS.GG.Kit.props is INCONCLUSIVE, NOT the hub's default" 3 \
  'will not substitute the hub' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# The receiver's own <FsggKitSkillRoots> beats the package default — receiver-owned, in its own tree,
# and what MSBuild would actually use. Proved by narrowing to one root: the other two stop being looked
# at rather than going 'missing'.
cat > "$PRECV/.config/kit/FS.GG.Kit.receiver.proj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <FsggKitSkillRoots>.claude/skills</FsggKitSkillRoots>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="FS.GG.Kit" Version="0.9.0" /></ItemGroup>
</Project>
EOF
expect_out "pin roots: the receiver project's own FsggKitSkillRoots beats the package default" 0 \
  "skill roots = [.]claude/skills [(]from [.]config/kit/FS[.]GG[.]Kit[.]receiver[.]proj" pin_check "$PRECV"
# ...and it really narrowed the subject: a root the receiver no longer declares is not looked for, so
# deleting it is NOT drift. A gate that kept the package default would call this missing.
rm -rf "$PRECV/.agents/skills"
expect_rc "pin roots: ...and a root the receiver does not declare is not verified (rc 0)" 0 \
  pin_check "$PRECV"
bash "$SYNC" "$PRECV" >/dev/null
write_proj 'Version="0.9.0"'

# A manifest destination that escapes the receiver root is a PRODUCER defect, not this tree's drift.
# Nothing here writes, so the cost is a verdict about the wrong subject rather than a clobbered file —
# which is still the failure this fabric keeps returning to, so it is asserted rather than trusted.
ESCSTAGE="$PINW/stage-escape"
cp -r "$STAGE" "$ESCSTAGE"
python3 - "$ESCSTAGE/kit-manifest.tsv" <<'PY'
import sys
path = sys.argv[1]
rows = []
for line in open(path, encoding="utf-8").read().splitlines():
    fields = line.split("\t")
    if fields and fields[0] == "client":
        fields[2] = "../../escaped-fsgg-coord"
    rows.append("\t".join(fields))
open(path, "w", encoding="utf-8").write("\n".join(rows) + "\n")
PY
pack_kit 0.9.4 "$ESCSTAGE"
write_proj 'Version="0.9.4"'
expect_out "pin: a manifest dest escaping the receiver root is INCONCLUSIVE (rc 3), not drift" 3 \
  'resolves outside the tree under test' pin_check "$PRECV"
write_proj 'Version="0.9.0"'

# The roster gate still SKIPS a non-receiver before any of this runs — it is the one hub read left, and
# it can only ever skip, never red.
expect_out "pin: the roster gate still skips a non-receiver (rc 0) before any pin is resolved" 0 \
  'does not receive coordination-kit' pin_check --repo FS-GG/.github "$PRECV"

echo "coordination-sync fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coordination-sync fixture FAILED"; exit 1; }
echo "coordination-sync fixture — OK"
