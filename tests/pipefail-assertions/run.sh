#!/usr/bin/env bash
# Fixture for scripts/check-pipefail-assertions.py (.github#2689).
#
# THE FAILURE LEGS COME FIRST, AND THAT IS THE DESIGN. This gate's specific way of failing open is
# the one epic #266 is about: it decides its own subject, and a scan that examines nothing reports
# exactly the same green as a scan over a clean tree. Worse, the gate is ABOUT assertions that
# compute an answer and discard it -- so a fixture that only ever fed it clean input would be the
# very defect it exists to catch, one level up. Leg 0 therefore proves the gate can say NO before
# any leg asks it to say yes, and legs 11-14 prove that "no verdict" is spelled differently from
# both answers.
#
# `set -uo pipefail`, deliberately WITHOUT errexit: almost every leg here runs the gate expecting a
# NON-ZERO exit, and under errexit each would kill the fixture instead of being measured. Every
# exit code is read UNPIPED, through `out="$(...)" || rc=$?`, because a pipeline's status is the
# LAST stage's -- reading a gate's verdict through `| tail` is how a worker nearly merged on a
# `LANDABLE_EXIT=0` that was really a 7, and this suite of all suites may not make that mistake.
#
# No assertion here consumes the status of a pipeline ending in an early-exiting reader, and no
# statement here is a bare `!` -- this fixture is inside its own gate's corpus and inside SC2251's,
# and a gate whose own fixture violates it is not a gate. Planted offender lines carry the
# `discarded-status-ok:` pragma OUTSIDE the quotes, as a shell comment on the source line, so the
# real-tree scan skips them while the synthetic files they write face the gate undefended.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-pipefail-assertions.py"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# No pipeline, so `pipefail` has nothing to mis-report. Same reason as tests/coord-engine-e2e.
contains() { case "$2" in *"$1"*) return 0 ;; *) return 1 ;; esac; }

WORK="$(mktemp -d)" || exit 2
trap 'rm -rf "$WORK"' EXIT

# ---- machinery -----------------------------------------------------------------------------------
# A throwaway git repo per leg: the gate enumerates with `git ls-files`, so an un-added file is
# invisible to it by design (an untracked scratch file is not this repo's shell).
tree_n=0
new_tree() {
  tree_n=$((tree_n + 1))
  TREE="$WORK/t$tree_n"
  mkdir -p "$TREE"
  git -C "$TREE" init -q
  git -C "$TREE" config user.email f@example.invalid
  git -C "$TREE" config user.name Fixture
  : > "$TREE/baseline.txt"
}

# write_shell <relpath> <body-line>...  -- a bash script that sets pipefail, plus the given lines.
write_shell() {
  local rel="$1"; shift
  mkdir -p "$TREE/$(dirname "$rel")"
  {
    echo '#!/usr/bin/env bash'
    echo 'set -euo pipefail'
    printf '%s\n' "$@"
  } > "$TREE/$rel"
}

commit_tree() { git -C "$TREE" add -A; git -C "$TREE" commit -qm f; }

# run_gate -> sets $RC and $OUT. UNPIPED: `|| rc=$?` reads the gate's own status, never a reader's.
RC=0; OUT=""
run_gate() {
  RC=0
  OUT="$(python3 "$GATE" --root "$TREE" --baseline baseline.txt 2>&1)" || RC=$?
}

# expect_rc <want> <label>
expect_rc() {
  if [ "$RC" -eq "$1" ]; then ok "$2 (rc=$RC)"; else bad "$2" "wanted rc=$1, got rc=$RC
$OUT"; fi
}

# =====================================================================================================
# 0. THE GATE CAN SAY NO. Before anything else, and over a corpus whose answer is known.
# =====================================================================================================
new_tree
write_shell "s.sh" 'if printf "%s" "$out" | grep -q NEEDLE; then echo hit; fi'  # discarded-status-ok: planted
commit_tree
run_gate
expect_rc 1 "0. a fresh offender with an empty baseline is a FINDING"
if contains "s.sh" "$OUT"; then
  ok "0b. the finding names the offending file"
else
  bad "0b. the finding names the offending file" "$OUT"
fi

# =====================================================================================================
# 1. EVERY BANNED SPELLING, one at a time. Missing `grep -m` and `head` was .github#2668's own
#    round-1 finding M3 -- both measured 141 on 30/30 runs -- so a guard blind to them is blind to
#    real offenders. Each is planted ALONE, so a leg that reds proves that spelling specifically.
# =====================================================================================================
spellings=(
  'if printf "%s" "$out" | grep -q NEEDLE; then echo hit; fi'      # discarded-status-ok: planted
  'if printf "%s" "$out" | grep -qE NEEDLE; then echo hit; fi'     # discarded-status-ok: planted
  'if printf "%s" "$out" | grep -qi NEEDLE; then echo hit; fi'     # discarded-status-ok: planted
  'if printf "%s" "$out" | grep -m1 NEEDLE >/dev/null; then :; fi' # discarded-status-ok: planted
  'first="$(printf "%s" "$out" | head)"'                           # discarded-status-ok: planted
  'excerpt="$(printf "%s" "$out" | head -c 200)"'                  # discarded-status-ok: planted
  'printf "%s" "$out" | head -5'                                   # discarded-status-ok: planted
)
caught=0
for sp in "${spellings[@]}"; do
  new_tree
  write_shell "s.sh" "$sp"
  commit_tree
  run_gate
  if [ "$RC" -eq 1 ]; then caught=$((caught + 1)); else bad "1. banned spelling not caught: $sp" "$OUT"; fi
done
if [ "$caught" -eq "${#spellings[@]}" ]; then
  ok "1. all ${#spellings[@]} banned spellings are caught individually"
fi

# =====================================================================================================
# 2. NO FALSE ACCUSATIONS. A lint nobody can satisfy is a lint somebody deletes (#238), and if the
#    safe spellings reded there would be nowhere for the remedy to point.
# =====================================================================================================
new_tree
write_shell "s.sh" \
  'case "$out" in *"$needle"*) found=1 ;; *) found=0 ;; esac' \
  'grep -qE -- "$re" <<<"$out"' \
  'prefix="${out:0:200}"' \
  'safe_a="$(printf "%s" "$out" | sed -n 1p)"' \
  'n="$(printf "%s" "$out" | grep -v skip | wc -l)"' \
  'printf "%s" "$out" | grep NEEDLE'
commit_tree
run_gate
expect_rc 0 "2. six genuinely safe spellings are NOT flagged"

# `| grep -v skip | wc -l` is the one worth calling out: the banned flag has to belong to the grep
# that ENDS the pipeline, and `[^|]*` cannot cross a pipe. A regex without that property flags it.
if contains "wc -l" "$OUT"; then
  bad "2b. a mid-pipeline grep must not be read as an early-exiting reader" "$OUT"
else
  ok "2b. a mid-pipeline grep is not read as an early-exiting reader"
fi

# =====================================================================================================
# 3. SCOPE. Without `pipefail` an earlier stage's SIGPIPE never reaches the pipeline's status, so
#    the shape is not this defect and flagging it would be a false accusation.
# =====================================================================================================
new_tree
mkdir -p "$TREE"
{
  echo '#!/usr/bin/env bash'
  echo 'set -eu'
  printf '%s\n' 'if printf "%s" "$out" | grep -q NEEDLE; then echo hit; fi'  # discarded-status-ok: planted
} > "$TREE/s.sh"
# A SECOND file that DOES set pipefail, and is clean. Without it this tree has no `pipefail` file at
# all and the gate answers 3 (no verdict) -- correctly, per leg 12, but that would make this leg
# measure leg 12's property instead of its own. Isolating the two is the point.
write_shell "other.sh" 'grep -q X <<<"$o"'
commit_tree
run_gate
expect_rc 0 "3. a file that does not set pipefail is out of scope"

# =====================================================================================================
# 4. COMMENTS AND THE PRAGMA.
# =====================================================================================================
new_tree
write_shell "s.sh" '#  printf "%s" "$out" | grep -q NEEDLE   <- quoted in a comment'  # discarded-status-ok: planted
commit_tree
run_gate
expect_rc 0 "4. a whole-line comment quoting the idiom is exempt"

new_tree
write_shell "s.sh" 'if printf "%s" "$o" | grep -q N; then :; fi # discarded-status-ok: it is fine'  # discarded-status-ok: planted
commit_tree
run_gate
expect_rc 0 "4b. the pragma WITH a reason suppresses the site"

# A suppression without a stated reason is a suppression nobody can review, so it suppresses nothing.
new_tree
write_shell "s.sh" 'if printf "%s" "$o" | grep -q N; then :; fi # discarded-status-ok:'  # discarded-status-ok: planted
commit_tree
run_gate
expect_rc 1 "4c. the pragma WITHOUT a reason suppresses NOTHING"

# =====================================================================================================
# 5-8. THE BASELINE RATCHET. This is the mechanism .github#2689's criterion 4 required be chosen and
#      stated: exact per-file counts, so the baseline can only ever shrink.
# =====================================================================================================
# The pragma goes on EVERY planted source line, and the planted lines are ASSIGNMENTS rather than
# `\`-continuations for that reason alone: a continuation line cannot carry a trailing comment, so
# there is nowhere to put the pragma. The gate caught exactly this in this fixture's own first
# draft -- four unpragma'd lines here, reported as a new offender file with no baseline entry.
plant_three() {
  local a b c
  a='if printf "%s" "$o" | grep -q A; then :; fi'  # discarded-status-ok: planted offender
  b='if printf "%s" "$o" | grep -q B; then :; fi'  # discarded-status-ok: planted offender
  c='if printf "%s" "$o" | grep -q C; then :; fi'  # discarded-status-ok: planted offender
  write_shell "s.sh" "$a" "$b" "$c"
}

new_tree; plant_three; echo "3 s.sh" > "$TREE/baseline.txt"; commit_tree; run_gate
expect_rc 0 "5. a count that matches the tree EXACTLY is green"

new_tree; plant_three; echo "2 s.sh" > "$TREE/baseline.txt"; commit_tree; run_gate
expect_rc 1 "6. MORE sites than the baseline allows is a finding (a new site arrived)"

new_tree; plant_three; echo "4 s.sh" > "$TREE/baseline.txt"; commit_tree; run_gate
expect_rc 1 "7. FEWER sites than the baseline claims is ALSO a finding (the baseline is stale)"
if contains "STALE" "$OUT"; then
  ok "7b. the stale finding says so, and prints the lower number to write"
else
  bad "7b. the stale finding says so" "$OUT"
fi

# The shrink leg, end to end: this is what "a baseline that shrinks" MEANS, and without it the
# mechanism would be an allowlist that merely exists.
new_tree; plant_three; echo "3 s.sh" > "$TREE/baseline.txt"; commit_tree; run_gate
if [ "$RC" -eq 0 ]; then
  kept='if printf "%s" "$o" | grep -q A; then :; fi'  # discarded-status-ok: planted, deliberately left
  # Two of the three converted to the here-string form the gate's remedy prints.
  write_shell "s.sh" "$kept" 'grep -q B <<<"$o"' 'grep -q C <<<"$o"'
  commit_tree
  run_gate
  if [ "$RC" -eq 1 ] && contains "STALE" "$OUT"; then
    ok "8. converting sites WITHOUT decrementing the baseline reds (the ratchet bites)"
    echo "1 s.sh" > "$TREE/baseline.txt"
    commit_tree
    run_gate
    expect_rc 0 "8b. the same conversion WITH its decrement, in one commit, is green"
  else
    bad "8. converting a site without decrementing must red" "$OUT"
  fi
else
  bad "8. setup for the shrink leg" "$OUT"
fi

new_tree; plant_three; echo "3 other.sh" > "$TREE/baseline.txt"; commit_tree; run_gate
expect_rc 1 "9. a baseline entry for a path that no longer exists is a finding"

# =====================================================================================================
# 10. DISCOVERY, and it is the #648 hole. Four of this repo's shell files are spelled as COMMANDS
#     with no extension -- `scripts/fsgg-coord` is the kit itself. An extension-only sweep reports
#     green having never opened them.
# =====================================================================================================
new_tree
mkdir -p "$TREE/scripts"
{
  echo '#!/usr/bin/env bash'
  echo 'set -euo pipefail'
  printf '%s\n' 'if printf "%s" "$o" | grep -q N; then :; fi'  # discarded-status-ok: planted
} > "$TREE/scripts/command-named"
commit_tree
run_gate
expect_rc 1 "10. a command-named shell file with NO extension is discovered"

# ...and no false accusation against files whose interpreter merely ENDS in `sh`, or is not shell at
# all. Linting a correct fish or python file is the #238 false accusation.
new_tree
mkdir -p "$TREE/scripts"
for interp in '#!/bin/zsh' '#!/usr/bin/env python3' '#!/bin/fish'; do
  name="$(printf '%s' "$interp" | tr -c 'a-z0-9' '-')"
  {
    printf '%s\n' "$interp"
    echo 'set -euo pipefail'
    printf '%s\n' 'if printf "%s" "$o" | grep -q N; then :; fi'  # discarded-status-ok: planted
  } > "$TREE/scripts/$name"
done
# One real shell file, so the corpus is non-empty and leg 11 is not what this measures.
write_shell "real.sh" 'grep -q X <<<"$o"'
commit_tree
run_gate
expect_rc 0 "10b. zsh/python/fish are not shell, and are not accused"

# =====================================================================================================
# 11-14. "I COULD NOT CHECK" IS NOT "IT PASSED" (#266), AND IS NOT "IT FAILED" (#320). Exit 3.
# =====================================================================================================
new_tree
commit_tree 2>/dev/null || git -C "$TREE" commit -q --allow-empty -m empty
run_gate
expect_rc 3 "11. a corpus with ZERO shell files is NO VERDICT, not green"

new_tree
mkdir -p "$TREE"
{ echo '#!/usr/bin/env bash'; echo 'set -eu'; echo 'echo hi'; } > "$TREE/s.sh"
commit_tree
run_gate
expect_rc 3 "12. shell files but NONE setting pipefail is NO VERDICT, not green"

new_tree
write_shell "s.sh" 'grep -q X <<<"$o"'
commit_tree
rm -f "$TREE/baseline.txt"
run_gate
expect_rc 3 "13. an unreadable/missing baseline is NO VERDICT, not green"

new_tree
write_shell "s.sh" 'grep -q X <<<"$o"'
echo 'not-a-count s.sh' > "$TREE/baseline.txt"
commit_tree
run_gate
expect_rc 3 "14. a malformed baseline entry is NO VERDICT, not green"

new_tree
write_shell "s.sh" 'grep -q X <<<"$o"'
printf '%s\n' '1 s.sh' '2 s.sh' > "$TREE/baseline.txt"
commit_tree
run_gate
expect_rc 3 "14b. a duplicated baseline entry is NO VERDICT, not green"

# =====================================================================================================
# 15. THE REAL TREE. Without this every leg above is synthetic, and the gate could pass its own
#     fixture while the shipped baseline rotted. NON-VACUITY is asserted too: a scan whose discovery
#     silently broke reports "OK" over an empty corpus just as cleanly as one over a clean tree, and
#     that is the failure this whole gate is aimed at.
# =====================================================================================================
real_rc=0
real_out="$(python3 "$GATE" --root "$REPO_ROOT" 2>&1)" || real_rc=$?
if [ "$real_rc" -eq 0 ]; then
  ok "15. this repo matches tests/pipefail-assertions/baseline.txt exactly"
else
  bad "15. this repo matches its baseline exactly (rc=$real_rc)" "$real_out"
fi

# The baseline is the gate's own evidence that it read something. A corpus that vanished would make
# every entry stale and red leg 15 -- but assert the floor directly, so a shrunk-to-nothing baseline
# cannot pass by agreeing with a broken scan.
baseline_files=0
baseline_sites=0
while read -r count _rest; do
  case "$count" in
    ''|\#*) continue ;;
  esac
  baseline_files=$((baseline_files + 1))
  baseline_sites=$((baseline_sites + count))
done < "$REPO_ROOT/tests/pipefail-assertions/baseline.txt"
if [ "$baseline_files" -ge 40 ] && [ "$baseline_sites" -ge 900 ]; then
  ok "15b. the baseline still describes a real corpus ($baseline_files files, $baseline_sites sites)"
else
  bad "15b. the baseline still describes a real corpus" \
      "$baseline_files file(s), $baseline_sites site(s) — floor is 40/900. If this is a genuine
mass conversion, lower the floor deliberately in the same commit."
fi

echo
echo "pipefail-assertions fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
echo "pipefail-assertions fixture: OK"
