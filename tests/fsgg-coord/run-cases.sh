#!/usr/bin/env bash
# The case runner for the fsgg-coord fixture — the tiered replacement for the run.sh monolith.
#
# Each file in cases/ sources lib/harness.sh, builds its OWN throwaway world (fixtures + a
# call-counting `gh` stub + an isolated cache), asserts, and ends in `harness_report`. This runner
# drives them, parses that report line, and tallies the suite.
#
# WHY TIERED, AND NOT ONE FILE. The monolith shared one cache and one gh-stub across 847 assertions
# in file order, so a case could only be reached through the side effects of every case above it.
# That hid real defects: the empty-RC_FILE fail-open (#344, reopened) is unreachable in run.sh
# because the publish window is always already spent by the time the #344 assertions run. A case
# that owns its world reaches it on the first call. Isolation is not tidiness here — it is coverage.
#
# THE CASES ARE THE CUT-OVER GATE (ADR-0034 §5). One case per historical defect, named for it, so
# the engine that replaces bash is judged against every path that has actually broken — not against
# three days of two workers agreeing on whatever items happened to float by.
#
#   bash tests/fsgg-coord/run-cases.sh              # all cases, parallel
#   bash tests/fsgg-coord/run-cases.sh 4x 51        # only cases matching a glob
#   SERIAL=1 bash tests/fsgg-coord/run-cases.sh     # one at a time, output interleaved live
#
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CASES="$HERE/cases"
JOBS="${JOBS:-$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)}"

# Select: every case, or only those whose basename matches a caller-supplied glob.
select_cases() {
  if [ "$#" -eq 0 ]; then
    find "$CASES" -maxdepth 1 -name '*.sh' | sort
    return
  fi
  local f b pat
  for f in $(find "$CASES" -maxdepth 1 -name '*.sh' | sort); do
    b="$(basename "$f" .sh)"
    for pat in "$@"; do
      # shellcheck disable=SC2254  — the glob is the point
      case "$b" in $pat|*$pat*) printf '%s\n' "$f"; break ;; esac
    done
  done
}

mapfile -t FILES < <(select_cases "$@")
[ "${#FILES[@]}" -gt 0 ] || { echo "no cases matched: $*" >&2; exit 2; }

OUT="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-coord-run.XXXXXX")"
trap 'rm -rf "$OUT"' EXIT

# CASE_NAME is what harness_report echoes; set it here so a case never has to name itself.
run_one() {
  local f="$1" b; b="$(basename "$f" .sh)"
  CASE_NAME="$b" bash "$f" >"$OUT/$b.log" 2>&1
  echo "$?" >"$OUT/$b.rc"
}
export -f run_one
export OUT

echo "fsgg-coord fixture — ${#FILES[@]} case(s), $( [ -n "${SERIAL:-}" ] && echo serial || echo "parallel x$JOBS" )"
echo

if [ -n "${SERIAL:-}" ]; then
  for f in "${FILES[@]}"; do run_one "$f"; done
else
  printf '%s\n' "${FILES[@]}" | xargs -P "$JOBS" -I{} bash -c 'run_one "$@"' _ {}
fi

# ---- tally -------------------------------------------------------------------------------------
# The report line is the contract: "<case> — N assertion(s): P passed, F failed". A case that dies
# before printing one (a `set -e` trip, a harness fault) is a SUITE failure, not a silent zero —
# a fixture that fails to run must never read as a fixture that found nothing.
total_pass=0 total_fail=0 bad_cases=0 no_report=0
for f in "${FILES[@]}"; do
  b="$(basename "$f" .sh)"; log="$OUT/$b.log"; rc="$(cat "$OUT/$b.rc" 2>/dev/null || echo 1)"
  line="$(grep -E '— [0-9]+ assertion\(s\):' "$log" 2>/dev/null | tail -1)"

  if [ -z "$line" ]; then
    printf '  \033[31mNO REPORT\033[0m  %-34s (exit %s)\n' "$b" "$rc"
    sed 's/^/      | /' "$log" | tail -12
    no_report=$((no_report + 1)); bad_cases=$((bad_cases + 1)); continue
  fi

  p="$(sed -E 's/.*: ([0-9]+) passed.*/\1/' <<<"$line")"
  fl="$(sed -E 's/.*, ([0-9]+) failed.*/\1/' <<<"$line")"
  total_pass=$((total_pass + p)); total_fail=$((total_fail + fl))

  if [ "$fl" -eq 0 ] && [ "$rc" -eq 0 ]; then
    printf '  \033[32mok\033[0m        %-34s %3s assertion(s)\n' "$b" "$p"
  else
    printf '  \033[31mFAIL\033[0m      %-34s %3s passed, \033[31m%s failed\033[0m\n' "$b" "$p" "$fl"
    grep -E '^FAIL' "$log" | sed 's/^/      | /'
    bad_cases=$((bad_cases + 1))
  fi
done

echo
echo "────────────────────────────────────────────────────────────────"
printf '%d case(s): %d assertion(s), %d passed, %d failed\n' \
  "${#FILES[@]}" "$((total_pass + total_fail))" "$total_pass" "$total_fail"
[ "$no_report" -eq 0 ] || printf '%d case(s) produced NO REPORT — treated as failures\n' "$no_report"

if [ "$bad_cases" -ne 0 ]; then
  echo "::error::fsgg-coord fixture: $bad_cases case(s) failed"
  exit 1
fi
echo "green."
