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
#
# ONE EXCEPTION, AND IT IS EARNED: a case that fails ONLY under parallelism, then passes when re-run
# ALONE, is a harness-isolation artifact, not a defect in the code under test. Each case owns its own
# $WORK, and the engine it drives is a fresh, stateless process per call — so the CLIENT's behaviour
# cannot depend on how many cases ran beside it. Only the harness's cross-case isolation can, and #761
# is exactly that: `41-residue`/`12-ready` exit 5 (the stub's board-fixture check) at random on CI's
# 2-core runner and nowhere else. So a first-pass failure is RE-RUN serially, once; if the isolated run
# is clean the case is FLAKY (surfaced loudly, so #761's root cause stays visible) and the suite stays
# green; if it fails again it is a real FAIL. A genuine bug fails both times. Set FSGG_NO_RETRY=1 to
# disable the retry (the flake hunt wants the raw parallel result).

# `verdict <basename>` -> echoes "ok:<passed>" | "fail:<passed>:<failed>" | "noreport:<rc>", reading the
# case's own captured log and rc. One place, so the first pass and the retry classify identically.
verdict() {
  local b="$1" log="$OUT/$b.log" rc line p fl
  rc="$(cat "$OUT/$b.rc" 2>/dev/null || echo 1)"
  line="$(grep -E '— [0-9]+ assertion\(s\):' "$log" 2>/dev/null | tail -1)"
  if [ -z "$line" ]; then echo "noreport:$rc"; return; fi
  p="$(sed -E 's/.*: ([0-9]+) passed.*/\1/' <<<"$line")"
  fl="$(sed -E 's/.*, ([0-9]+) failed.*/\1/' <<<"$line")"
  if [ "$fl" -eq 0 ] && [ "$rc" -eq 0 ]; then echo "ok:$p"; else echo "fail:$p:$fl"; fi
}

total_pass=0 total_fail=0 bad_cases=0 no_report=0 flaky=0
flaky_names=""
for f in "${FILES[@]}"; do
  b="$(basename "$f" .sh)"
  v="$(verdict "$b")"

  # A first-pass failure in a PARALLEL run gets one isolated retry before it is believed.
  retried=""
  if [ -z "${SERIAL:-}" ] && [ -z "${FSGG_NO_RETRY:-}" ] && [ "${v%%:*}" != "ok" ]; then
    run_one "$f"          # re-run this case ALONE, into the same $OUT/$b.{log,rc}
    retried=1
    v2="$(verdict "$b")"
    if [ "${v2%%:*}" = "ok" ]; then
      # Failed under parallelism, clean in isolation → a harness-isolation artifact (#761).
      p="${v2#ok:}"
      printf '  \033[33mFLAKY\033[0m     %-34s %3s assertion(s) — failed under parallelism, clean when re-run alone (#761)\n' "$b" "$p"
      total_pass=$((total_pass + p)); flaky=$((flaky + 1)); flaky_names="$flaky_names $b"
      continue
    fi
    v="$v2"             # the retry also failed — it is real; report the retry's result
  fi

  case "$v" in
    ok:*)
      p="${v#ok:}"
      total_pass=$((total_pass + p))
      printf '  \033[32mok\033[0m        %-34s %3s assertion(s)\n' "$b" "$p"
      ;;
    noreport:*)
      rc="${v#noreport:}"
      printf '  \033[31mNO REPORT\033[0m  %-34s (exit %s%s)\n' "$b" "$rc" "$([ -n "$retried" ] && echo ", twice")"
      sed 's/^/      | /' "$OUT/$b.log" | tail -12
      no_report=$((no_report + 1)); bad_cases=$((bad_cases + 1))
      ;;
    fail:*)
      rest="${v#fail:}"; p="${rest%%:*}"; fl="${rest#*:}"
      total_pass=$((total_pass + p)); total_fail=$((total_fail + fl))
      printf '  \033[31mFAIL\033[0m      %-34s %3s passed, \033[31m%s failed\033[0m%s\n' \
        "$b" "$p" "$fl" "$([ -n "$retried" ] && echo " (failed twice)")"
      grep -E '^FAIL' "$OUT/$b.log" | sed 's/^/      | /'
      bad_cases=$((bad_cases + 1))
      ;;
  esac
done

echo
echo "────────────────────────────────────────────────────────────────"
printf '%d case(s): %d assertion(s), %d passed, %d failed\n' \
  "${#FILES[@]}" "$((total_pass + total_fail))" "$total_pass" "$total_fail"
[ "$no_report" -eq 0 ] || printf '%d case(s) produced NO REPORT — treated as failures\n' "$no_report"
if [ "$flaky" -ne 0 ]; then
  printf '\033[33m%d case(s) were FLAKY (green only when re-run alone):%s — see #761. The suite stays green, but the harness-isolation root cause is real and unfixed.\033[0m\n' "$flaky" "$flaky_names"
  echo "::warning::fsgg-coord fixture: $flaky flaky case(s) (#761) —$flaky_names — passed on isolated retry"
fi

if [ "$bad_cases" -ne 0 ]; then
  echo "::error::fsgg-coord fixture: $bad_cases case(s) failed"
  exit 1
fi
echo "green."
