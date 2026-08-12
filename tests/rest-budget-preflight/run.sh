#!/usr/bin/env bash
# Fixture for the "Check REST budget before scanning" (`id: budget`) step in
# .github/workflows/coord-board-reconcile.yml (.github#2352 AC-3, corrected by .github#2355).
#
# THE DEFECT THIS CLOSES. `.github#894` and `.github#907` measured, independently, twice, that
# `GET /rate_limit`'s JSON BODY (`resources.core.remaining`) disagrees with reality on this account —
# it reported a healthy 2431/2320 while a REAL REST call 403'd with `remaining: 0` in the same window.
# A pre-flight that trusts that body can read "healthy", set `defer=false`, and let a ~2000-item board
# scan START against a budget that is already dead — burning the REST the fleet's live
# `claim`/`take`/`heartbeat` calls need, which is the exact resource AC-3 exists to protect. The fix
# reads a REAL REST call's OWN response headers (`X-RateLimit-Remaining`) instead — the same counter a
# live `claim`/`take` would see 403 against, per `docs/coordination/graphql-budget.md`'s own conclusion.
#
# WHY IT TESTS THE SHIPPED SCRIPT, NOT A COPY. The legs below extract the `budget` step's `run:` block
# from the REAL coord-board-reconcile.yml and execute it, mirroring tests/dispatch-preflight's rationale:
# a re-typed copy would keep passing after someone quietly reintroduces the `/rate_limit`-body read this
# fixture exists to catch.
#
# No network, no git, no runner: `curl` is stubbed to hand back canned response headers.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WF="$REPO_ROOT/.github/workflows/coord-board-reconcile.yml"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/rest-budget-preflight-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() {
  echo "FAIL  $1"
  if [ -n "${2:-}" ]; then printf '%s\n' "$2" | sed 's/^/    | /'; fi
  failcount=$((failcount+1))
}

[ -f "$WF" ] || { echo "FAIL  coord-board-reconcile.yml is missing entirely — nothing to test."; exit 1; }

# ---- pull the REAL budget-step script + raw text out of the REAL workflow -------------------------
SCRIPT="$WORK/budget.sh"

python3 - "$WF" "$SCRIPT" <<'PY'
import sys, yaml

wf_path, script_out = sys.argv[1], sys.argv[2]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)

jobs = wf.get("jobs") or {}
reconcile = jobs.get("reconcile")
if reconcile is None:
    sys.exit("the `reconcile` job is GONE from coord-board-reconcile.yml.")

steps = reconcile.get("steps") or []
budget_steps = [s for s in steps if s.get("id") == "budget"]
if not budget_steps:
    sys.exit("the `budget` step (id: budget) is GONE from coord-board-reconcile.yml — the pre-flight has been deleted.")
if len(budget_steps) > 1:
    sys.exit("more than one step carries id: budget — ambiguous, cannot select the pre-flight uniquely.")
step = budget_steps[0]

with open(script_out, "w", encoding="utf-8") as fh:
    fh.write(step["run"])
PY

RAW="$(cat "$WF")"

# ---- structural: the honest-source fix cannot silently regress back to the untrustworthy body -----
# .github#894/#907's failure mode is a call to `/rate_limit` whose BODY (`resources.core.remaining`)
# over-reports. Asserting the script text never makes that call is what would catch a future edit that
# quietly reintroduces it (a well-meaning "simplify this" that reverts .github#2355).
if printf '%s' "$(cat "$SCRIPT")" | grep -q 'api\.github\.com/rate_limit'; then
  bad "S1  the budget step never calls GET /rate_limit" \
      "the script calls /rate_limit again — that endpoint's BODY is exactly what .github#894/#907 measured to disagree with reality on this account. .github#2355 replaced it with a real REST call's own headers; reintroducing it silently regresses that fix."
else
  ok "S1  the budget step never calls GET /rate_limit"
fi

if printf '%s' "$(cat "$SCRIPT")" | grep -qi 'resources\.core\.remaining'; then
  bad "S2  the budget step never parses resources.core.remaining" \
      "the script still parses a rate_limit-shaped JSON body field. The honest signal is a real call's own X-RateLimit-Remaining response header, not a computed body field (.github#2355)."
else
  ok "S2  the budget step never parses resources.core.remaining"
fi

if printf '%s' "$(cat "$SCRIPT")" | grep -qi 'x-ratelimit-remaining'; then
  ok "S3  the budget step reads a real response's X-RateLimit-Remaining header"
else
  bad "S3  the budget step reads a real response's X-RateLimit-Remaining header" \
      "no X-RateLimit-Remaining header read found — the step no longer reads the honest per-call signal .github#894/#907/graphql-budget.md name."
fi

# AC-3: the pre-flight's own comments must say the exit-75 mapping (not this step) is load-bearing.
# Matched on a phrase specific to that statement, not the bare word "load-bearing" — an unrelated
# comment elsewhere in this file (the issue_comment marker-prefix filter) already uses that word for a
# different reason, and a substring-only match would pass on a file that never states AC-3 at all.
if printf '%s' "$RAW" | grep -qi 'best-effort floor, not the safety net'; then
  ok "S4  the workflow states AC-3's load-bearing safety net explicitly"
else
  bad "S4  the workflow states AC-3's load-bearing safety net explicitly" \
      "acceptance criterion 3 (.github#2355) requires the workflow to say explicitly that the exit-75 mapping, not this pre-flight, is what actually protects against a wrong verdict here. No such statement was found."
fi

# ---- behaviour: run the REAL extracted script against a stubbed curl ------------------------------
STUB="$WORK/bin"; mkdir -p "$STUB"

# make_curl <exit-code> [header-line...] -> stub prints the given header lines to stdout (as -D -
# would) and exits with the given code. The real step discards the body (-o /dev/null) and reads only
# headers, so the stub need not emit one.
make_curl() {
  local rc="$1"; shift
  {
    echo '#!/usr/bin/env bash'
    for l in "$@"; do
      printf 'echo %q\n' "$l"
    done
    printf 'exit %s\n' "$rc"
  } > "$STUB/curl"
  chmod +x "$STUB/curl"
}

# run_budget -> writes GITHUB_OUTPUT contents + combined stdout/stderr, echoes the script's own exit code
run_budget() {
  : > "$WORK/step_output"
  GITHUB_TOKEN="tok" GITHUB_REPOSITORY="FS-GG/.github" GITHUB_OUTPUT="$WORK/step_output" \
  PATH="$STUB:$PATH" \
    bash "$SCRIPT" >"$WORK/out" 2>&1 && echo 0 || echo $?
}

out_field() { sed -n "s/^$1=//p" "$WORK/step_output" | tail -1; }

# healthy <name> <curl-rc> <header-line...>  — must publish defer=false
healthy() {
  local name="$1" rc="$2"; shift 2
  make_curl "$rc" "$@"
  local sc; sc="$(run_budget)"
  if [ "$sc" != 0 ]; then bad "$name" "$(printf 'the step itself exited %s (it always exits 0, set +e / exit 0)\n%s' "$sc" "$(cat "$WORK/out")")"; return; fi
  local defer; defer="$(out_field defer)"
  [ "$defer" = "false" ] && ok "$name" \
    || bad "$name" "$(printf 'expected defer=false, got defer=%s\nremaining=%s reserve=%s\n%s' "$defer" "$(out_field remaining)" "$(out_field reserve)" "$(cat "$WORK/out")")"
}

# deferred <name> <curl-rc> <reason-pattern> <header-line...>  — must publish defer=true with a reason
# matching the pattern, and warn via the reason text (not this fixture — the "Defer" step consumes it).
deferred() {
  local name="$1" rc="$2" pat="$3"; shift 3
  make_curl "$rc" "$@"
  local sc; sc="$(run_budget)"
  if [ "$sc" != 0 ]; then bad "$name" "$(printf 'the step itself exited %s (it always exits 0, set +e / exit 0)\n%s' "$sc" "$(cat "$WORK/out")")"; return; fi
  local defer reason; defer="$(out_field defer)"; reason="$(out_field defer_reason)"
  if [ "$defer" != "true" ]; then
    bad "$name" "$(printf 'expected defer=true, got defer=%s\nremaining=%s reserve=%s\n%s' "$defer" "$(out_field remaining)" "$(out_field reserve)" "$(cat "$WORK/out")")"
    return
  fi
  printf '%s' "$reason" | grep -qi -- "$pat" && ok "$name" \
    || bad "$name" "$(printf 'deferred, but not for the stated reason (want %q):\n  defer_reason=%s' "$pat" "$reason")"
}

echo "== behaviour: honest per-call headers drive the verdict, not a rate_limit body =="

# B1/B2: comfortably above and comfortably below the 750 reserve, via the real header this call gets.
healthy  "B1  remaining=4562 (well above reserve)         -> defer=false" 0 \
  'HTTP/2 200' 'x-ratelimit-remaining: 4562' 'x-ratelimit-reset: 1786550000'
deferred "B2  remaining=500 (well below reserve)           -> defer=true, low-budget reason" 0 'low' \
  'HTTP/2 200' 'x-ratelimit-remaining: 500' 'x-ratelimit-reset: 1786550000'

# B3/B4: the exact boundary — the original semantics (`-lt`, not `-le`) must survive the source swap.
healthy  "B3  remaining=750 (exactly at reserve)           -> defer=false" 0 \
  'HTTP/2 200' 'x-ratelimit-remaining: 750'
deferred "B4  remaining=749 (one below reserve)             -> defer=true" 0 'low' \
  'HTTP/2 200' 'x-ratelimit-remaining: 749'

# B5: THE .github#894/#907 SHAPE ITSELF. A real call is already 403'ing (budget is actually zero) —
# GitHub still returns X-RateLimit-Remaining: 0 on the 403 body's own headers, which is the "honest
# reading" graphql-budget.md names. Whatever a hypothetical /rate_limit body might have claimed
# alongside it is irrelevant: this script no longer reads that body at all (S1/S2 above).
deferred "B5  a REAL call 403's with remaining: 0           -> defer=true, low-budget reason" 0 'low' \
  'HTTP/2 403' 'x-ratelimit-remaining: 0' 'x-ratelimit-reset: 1786550000'

# B6/B7: unreadable — curl fails outright (network error) or returns headers with no such field at
# all. Both must fail CLOSED (defer=true), same as the original's "could not read" leg.
deferred "B6  curl fails outright (network error)           -> defer=true, unreadable reason" 7 'could not read'
deferred "B7  headers present but no X-RateLimit-Remaining  -> defer=true, unreadable reason" 0 'could not read' \
  'HTTP/2 200' 'content-type: application/json'

# B8: header matching is case-insensitive (HTTP header names are case-insensitive, and real servers/
# proxies vary casing) — a script that greps only the exact lower-case spelling silently degrades to B7
# on every response that capitalises it differently, which is common (`X-RateLimit-Remaining`).
healthy "B8  header name case (X-RateLimit-Remaining)       -> defer=false, still read" 0 \
  'HTTP/2 200' 'X-RateLimit-Remaining: 4000'

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1
