#!/usr/bin/env bash
# case: verify paths boundary
# tier: full
# covers: verify-paths
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="23-verify-paths-boundary"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- verify-paths: did the PR stay inside the declared touch-set? ------------------------------
cat >"$FIXTURES/pr-7.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":7}
JSON
cat >"$FIXTURES/pr-files-7.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"tests/Scene/GraphTests.fs"}]
JSON
cat >"$FIXTURES/pr-8.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":8}
JSON
cat >"$FIXTURES/pr-files-8.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"src/Audio/Mixer.fs"},{"filename":"README.md"}]
JSON
assert_contains "verify-paths: a PR inside its touch-set is OK" "FSGG-PATHS OK" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD 2>/dev/null)"
drift="$(pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD 2>&1 || true)"
assert_contains "verify-paths: drift is reported with a count" "FSGG-PATHS DRIFT — PR #8 touches 2 file(s) outside" "$drift"
assert_contains "verify-paths: names the offending files"      "src/Audio/Mixer.fs" "$drift"
assert_contains "verify-paths: does not flag files inside the touch-set" "src/Scene/Graph.fs" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD 2>/dev/null; echo 'src/Scene/Graph.fs')"
assert_contains "verify-paths: points at the remedy" "fsgg-coord widen" "$drift"
assert_fails "verify-paths: drift exits non-zero by default" pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD
assert_contains "verify-paths --warn: reports drift but exits 0 (the advisory CI gate)" "FSGG-PATHS DRIFT" \
  "$(pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
pw verify-paths --pr 8 --repo FS-GG/FS.GG.SDD --warn >/dev/null 2>&1 \
  && ok "verify-paths --warn: exit 0" || bad "verify-paths --warn: exit 0" "non-zero exit under --warn"

# A PR with nothing to verify against is SKIP, never a silent OK — CI must not stamp "stays inside
# its touch-set" on a PR that never declared one.
cat >"$FIXTURES/pr-9.json" <<'JSON'
{"head":{"ref":"chore/no-linked-issue"},"number":9}
JSON
cat >"$FIXTURES/pr-files-9.json" <<'JSON'
[{"filename":"README.md"}]
JSON
cat >"$FIXTURES/pr-10.json" <<'JSON'
{"head":{"ref":"item/72-no-touch-set-declared"},"number":10}
JSON
cat >"$FIXTURES/pr-files-10.json" <<'JSON'
[{"filename":"src/Whatever.fs"}]
JSON
skip9="$(pw verify-paths --pr 9 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
assert_contains "verify-paths --warn: an unlinked PR is SKIP, not OK" "FSGG-PATHS SKIP" "$skip9"
case "$skip9" in *"FSGG-PATHS OK"*) bad "verify-paths --warn: SKIP is not mistaken for OK" "OK leaked into a SKIP verdict" ;; *) ok "verify-paths --warn: SKIP is not mistaken for OK" ;; esac
assert_contains "verify-paths --warn: an undeclared touch-set is SKIP" "declares no 'Paths:' touch-set" \
  "$(pw verify-paths --pr 10 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
assert_fails "verify-paths: an unlinked PR fails without --warn" pw verify-paths --pr 9 --repo FS-GG/FS.GG.SDD

# `SKIP` must mean "I asked, and there is nothing to check" — never "I could not ask" (.github#322,
# child (j) of #266). When the head-ref read 502s and the GraphQL fallback then answers healthily
# that the PR closes no issue — the normal case, since ADR-0021's convention is the branch name, not
# a `Closes:` line — an unreachable subject used to be indistinguishable from an unlinked PR: SKIP,
# rc=0, gate green, sticky comment deleted. The touch-set went unchecked and nothing said so.
#
# `--warn` downgrades the *drift verdict* to advisory. It does not license inventing a verdict from
# an unanswered query, so this fails closed under `--warn` too — which is exactly the rc the
# touch-set-drift gate keeps rather than `|| true`-ing away.
vp502() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
            verify-paths --pr 7 --repo FS-GG/FS.GG.SDD "$@" 2>&1; }
out502="$(vp502 --warn || true)"
case "$out502" in
  *"FSGG-PATHS"*) bad "verify-paths: an unreachable head ref reaches NO verdict" "invented a verdict: $out502" ;;
  *) ok "verify-paths: an unreachable head ref reaches NO verdict" ;;
esac
assert_contains "verify-paths: ...and says the head ref could not be read" "cannot read PR #7's head ref" "$out502"
assert_contains "verify-paths: ...and refuses to guess rather than skipping" "refusing to guess" "$out502"
assert_fails "verify-paths: an unreachable head ref fails closed under --warn" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --warn
assert_fails "verify-paths: an unreachable head ref fails closed by default" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD
# An explicit --issue needs no head ref, so it must not be dragged down by the failing lookup: the
# fix must guard the CALL, not the whole resolution step.
assert_contains "verify-paths: --issue bypasses the head-ref read entirely" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_PR_GET=7 bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"

# ---- the repo boundary: a verdict may only be printed on ONE repo's PR vs THAT repo's issue (#479) --
# `--pr` used to resolve against the repo you were STANDING IN while `--issue` carried a repo of its
# own. Nothing compared them. So `verify-paths --pr 48 --issue 'FS.GG.Audio#42'` run from `.github`
# read `.github`'s PR 48 — a closed, unrelated PR — diffed it against Audio's touch-set, and printed
# `FSGG-PATHS DRIFT`, naming a file that was in neither the PR nor the repo. The inverse is worse: a
# genuinely drifting PR in another repo reports `FSGG-PATHS OK` whenever the same-numbered PR in the
# CWD happens to be clean — the guard passing, with confidence, on a subject it never looked at. And
# it is load-bearing: `.github/workflows/touch-set-drift.yml` greps this very output for its verdict.
#
# These tests are the mismatched leg #266 asks for. Note what makes them possible: the stub logs the
# REPO of each PR read, because the payload cannot betray the bug — the fixtures are keyed by number
# alone, so the stub is precisely as repo-blind as the code was. The subject has to be asserted from
# the request, or the test would pass on the wrong repo exactly the way the tool did.
no_verdict() { # <name> <output> — the ONLY safe outcome across a boundary is no OK/DRIFT at all
  case "$2" in
    *"FSGG-PATHS OK"*|*"FSGG-PATHS DRIFT"*) bad "$1" "printed a verdict across a repo boundary: $2" ;;
    *) ok "$1" ;;
  esac
}
mism="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
          verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' 2>&1 || true)"
no_verdict "verify-paths: --repo and --issue in different repos reach NO verdict" "$mism"
assert_contains "verify-paths: ...and names BOTH repos it was asked to straddle" "FS-GG/FS.GG.Rendering" "$mism"
assert_contains "verify-paths: ...and says the touch-set was not checked" "touch-set was NOT checked" "$mism"
assert_fails "verify-paths: a cross-repo mismatch fails by default" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70'
# --warn downgrades a VERDICT to advisory. It does not license a verdict on the wrong subject, so the
# mismatch still fails closed under it — the rc the touch-set-drift gate keeps rather than `|| true`-ing.
assert_fails "verify-paths: a cross-repo mismatch fails closed under --warn too" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
    verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' --warn
no_verdict "verify-paths: ...and still reaches no verdict under --warn" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.Rendering#70' --warn 2>&1 || true)"

# With no --repo, the ISSUE decides — not the checkout. `gh repo view` (the stub) says FS-GG/FS.GG.SDD,
# so the old code would have read PR 7 from SDD; the acceptance criterion is that it reads it from
# Rendering, the repo the caller actually named. Assert on the REQUEST, not the verdict: the fixtures
# would serve an identical, healthy `FSGG-PATHS OK` either way — which is the whole trap.
: >"$GH_LOG"
PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
  verify-paths --pr 7 --issue 'FS-GG/FS.GG.Rendering#70' >/dev/null 2>&1 || true
assert_contains "verify-paths: --issue decides the repo when --repo is absent" \
  "pr-files FS-GG/FS.GG.Rendering 7" "$(cat "$GH_LOG")"
# Scoped to the PR-READ lines, which is what this assertion is actually about. Grepping the whole log
# for the substring would go red the day any unrelated, legitimate SDD read joined this flow — and
# since #494 every issue read logs its repo too, so that log is no longer a proxy for "the PR's repo".
case "$(grep -E '^pr-(get|files) ' "$GH_LOG" || true)" in
  *"FS.GG.SDD"*) bad "verify-paths: ...and the CHECKOUT's repo is never consulted for the PR" \
                     "read the PR from the checkout's repo, not the issue's: $(cat "$GH_LOG")" ;;
  *)             ok  "verify-paths: ...and the CHECKOUT's repo is never consulted for the PR" ;;
esac

# ---- #430: the repo comes off the REMOTE, and a rate limit is reported AS a rate limit -----------
# With neither --repo nor --issue, verify-paths used to derive the repo from `gh repo view` — a
# GraphQL call — and read its empty result as proof of "not inside a GitHub checkout". But empty did
# not mean that. It meant the call failed and `2>/dev/null || true` had just discarded the reason.
# The reason it was usually hiding was an EXHAUSTED budget: the steady state of the N-agents-on-one-
# account fan-out this kit exists to serve. So at the merge boundary, a worker in a perfectly good
# checkout was sent to go debug their checkout, over a limit that clears on its own.
#
# `pw` runs in the fixture's own cwd; these need to stand somewhere specific, so combine it with run_at.
pw_at() { local d="$1"; shift; ( cd "$d" && PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@" ); }
# A git checkout with NO remote at all — the only case in which `gh` is still worth asking.
CO_NOREMOTE="$WORK/co-noremote"; mkdir -p "$CO_NOREMOTE"; git -C "$CO_NOREMOTE" init -q >/dev/null 2>&1

# (a) The remote decides. Assert on the CALL, not the verdict: the stub's `gh repo view` also says
#     FS.GG.SDD, so both the old code and the new one reach an identical healthy verdict here — the
#     whole trap. What changed is that no GraphQL was spent to get it.
: >"$GH_LOG"
pw_at "$CO_SDD" verify-paths --pr 7 >/dev/null 2>&1 || true
assert_contains "#430: the repo is derived from the git remote" \
  "pr-files FS-GG/FS.GG.SDD 7" "$(cat "$GH_LOG")"
case "$(cat "$GH_LOG")" in
  *repo-view*) bad "#430: ...and 'gh repo view' is never called for it" \
                   "spent a GraphQL repo view: $(cat "$GH_LOG")" ;;
  *)           ok  "#430: ...and 'gh repo view' is never called for it" ;;
esac

# (b) THE ACCEPTANCE CRITERION: it now reaches a verdict on an exhausted budget, from a good checkout.
assert_contains "#430: ...so it still reaches a verdict when the GraphQL budget is EXHAUSTED" \
  "FSGG-PATHS OK" "$(GH_FAIL_REPO_VIEW=rate pw_at "$CO_SDD" verify-paths --pr 7 2>&1 || true)"

# (c) No remote AND a dead budget: `gh` is the honest fallback, and its failure is now EVIDENCE.
#     A rate limit is named as one and exits EX_RATE (75) — the code a worker loop already backs off
#     on — rather than being dressed up as a fact about the reader's working directory.
rc430=0; out430="$(GH_FAIL_REPO_VIEW=rate pw_at "$CO_NOREMOTE" verify-paths --pr 7 2>&1)" || rc430=$?
assert_eq       "#430: no remote + dead budget exits EX_RATE (75), not a generic 1" "75" "$rc430"
assert_contains "#430: ...and names the rate limit"    "GraphQL budget EXHAUSTED" "$out430"
case "$out430" in
  *"not inside a GitHub checkout"*)
    bad "#430: ...and NEVER blames the checkout for it" "blamed the checkout for a rate limit: $out430" ;;
  *) ok "#430: ...and NEVER blames the checkout for it" ;;
esac

# (c2) …and it classifies on gh's WHOLE output, not just its last line. A rate limit followed by a
#      trailing hint is still a rate limit. Reading only the tail here would drop the worker onto the
#      generic arm — exit 1 instead of EX_RATE — and their loop would never back off: the same bug
#      one level down, a diagnosis built on a PARTIAL error rather than a discarded one.
# The ( ) is load-bearing: an env prefix on a FUNCTION call can persist in the caller's shell, which
# would leak `rate2` into every test below. The other arms are already inside a `$( )` subshell.
rc430c=0; ( GH_FAIL_REPO_VIEW=rate2 pw_at "$CO_NOREMOTE" verify-paths --pr 7 >/dev/null 2>&1 ) || rc430c=$?
assert_eq "#430: a rate limit that is not gh's LAST line still exits EX_RATE (75)" "75" "$rc430c"

# (d) A gh failure that is genuinely NOT a rate limit says what gh actually said — the other half of
#     the issue. A diagnosis built on a discarded error is how this bug happened in the first place.
out430b="$(GH_FAIL_REPO_VIEW=other pw_at "$CO_NOREMOTE" verify-paths --pr 7 2>&1 || true)"
assert_contains "#430: a non-rate-limit gh failure reports gh's OWN words" \
  "could not determine current repository" "$out430b"
assert_contains "#430: ...and points at the explicit remedy" "--repo FS-GG/<repo>" "$out430b"

# (e) And the old message SURVIVES exactly where it was always true: gh succeeded and named nothing.
#     Only now it is a conclusion this code established, rather than one it inferred from an error it
#     had thrown away.
assert_contains "#430: gh SUCCEEDS and names nothing -> 'not inside a checkout' is an EARNED verdict" \
  "not inside a GitHub checkout" \
  "$(GH_FAIL_REPO_VIEW=empty pw_at "$CO_NOREMOTE" verify-paths --pr 7 2>&1 || true)"


harness_report
