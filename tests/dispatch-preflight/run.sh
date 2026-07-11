#!/usr/bin/env bash
# Fixture for the `preflight` job in .github/workflows/lockfile-sync.yml — the gate that asserts an
# adopting repo can actually AUTHENTICATE before the sync tries to (.github#482, epic #266).
#
# THE DEFECT THIS CLOSES. `required: true` on a workflow_call secret means PROVIDED, not NON-EMPTY.
# A caller forwarding `${{ secrets.FSGG_DISPATCH_APP_ID }}` for a secret its repo cannot see passes
# the EMPTY STRING and satisfies `required` — so an unprovisioned repo sails past every check and
# dies at create-github-app-token, inside somebody's unrelated dependency-bump PR, naming neither
# the secret nor the App. Two of six adopters were in exactly that state and nobody knew: FS.GG.Game
# (live) and FS.GG.Audio (inert). Provisioning is .github#468; this is the gate that would have said
# so on the first PR after adoption instead of months later.
#
# WHY IT TESTS THE SHIPPED SCRIPT, NOT A COPY. The legs below extract the preflight step's `run:`
# block from the REAL lockfile-sync.yml and execute it. A re-typed copy would keep passing after
# someone edits the workflow — which is the fails-open shape epic #266 exists to close, rebuilt
# inside the fixture meant to close it. Same reason tests/pin-coherence copies the REAL default.json.
#
# Every negative leg asserts the REASON, not merely a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than from
# the thing under test. So `red` and `amber` both take a required pattern.
#
# The STRUCTURAL legs are the ones that matter most. A truth table only proves the script is right;
# it cannot prove the script still RUNS. Legs S1-S5 assert the gate cannot be skipped into green:
# preflight carries no `if:` and no `continue-on-error`, `sync` still `needs:` it, the two copies of
# the sync condition have not drifted, and the secret VALUES never reach the environment.
#
# No network, no git, no runner. Pure shell + the workflow's own YAML.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SYNC_WF="$REPO_ROOT/.github/workflows/lockfile-sync.yml"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/dispatch-preflight-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() {
  echo "FAIL  $1"
  # `[ -n ... ] && printf` would return 1 on an empty $2 and, under `set -e`, abort the whole fixture
  # mid-run — truncating the remaining legs and the summary. Spell it as an `if`.
  if [ -n "${2:-}" ]; then printf '%s\n' "$2" | sed 's/^/    | /'; fi
  failcount=$((failcount+1))
}

[ -f "$SYNC_WF" ] || { echo "FAIL  lockfile-sync.yml is missing entirely — nothing to test."; exit 1; }

# ---- pull the REAL preflight script + structure out of the REAL workflow ------------------------
SCRIPT="$WORK/preflight.sh"
META="$WORK/meta.json"

python3 - "$SYNC_WF" "$SCRIPT" "$META" <<'PY'
import json, sys, yaml

wf_path, script_out, meta_out = sys.argv[1], sys.argv[2], sys.argv[3]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)

jobs = wf.get("jobs") or {}
pre, sync = jobs.get("preflight"), jobs.get("sync")
if pre is None:
    sys.exit("the `preflight` job is GONE from lockfile-sync.yml — the gate has been deleted.")
if sync is None:
    sys.exit("the `sync` job is gone from lockfile-sync.yml.")

all_steps = pre.get("steps") or []
run_steps = {s.get("id"): s for s in all_steps if "run" in s}

# preflight now asserts BOTH prerequisites, so it has two `run:` legs (.github#522):
#   secrets       prerequisite 2 — are the org secrets visible here? (no token, no API call)
#   installation  prerequisite 1 — is this repo IN the App installation? (owner-scoped mint + enumerate)
# Select them BY ID. A positional pick would silently follow a reordering, and an assertion that
# quietly starts testing a different step is the fixture rebuilding the fail-open it exists to catch.
for want in ("secrets", "installation"):
    if want not in run_steps:
        sys.exit(f"the `{want}` step is GONE from preflight — that prerequisite is no longer asserted.")
step = run_steps["secrets"]
install_step = run_steps["installation"]

with open(script_out, "w", encoding="utf-8") as fh:
    fh.write(step["run"])
with open(script_out + ".install", "w", encoding="utf-8") as fh:
    fh.write(install_step["run"])

env = step.get("env") or {}
needs = sync.get("needs")

# `continue-on-error` — the rule, restated precisely rather than loosened (.github#522).
#
# The original rule was "no continue-on-error ANYWHERE in preflight", because it lets a gate be
# skipped into green. Prerequisite 1 needs a token, and a token mint is a `uses:` step that FAILS
# HARD when the key is wrong — which would red every unrelated PR in an under-provisioned repo, the
# exact blast-radius mistake the split-severity design exists to avoid. So the mint carries
# `continue-on-error` and hands control to `installation`, which decides the severity itself.
#
# That is safe if and ONLY if the step it hands to cannot pass silently. So the invariant becomes:
#   * the JOB may never carry continue-on-error;
#   * no `run:` step (the steps that actually RENDER A VERDICT) may carry it;
#   * a `uses:` step may, and the truth table below proves `installation` fails CLOSED on the empty
#     token that a swallowed mint failure produces.
# A blanket ban here would have forced the gate to choose between "cannot check prerequisite 1" and
# "reds unrelated PRs" — so state the rule that is actually meant, and test it.
meta = {
    # `if:` on the gate itself would let it be skipped into green — the #266 signature.
    "preflight_has_if": "if" in pre,
    "preflight_continue_on_error": bool(pre.get("continue-on-error")),
    # NO verdict-rendering step may swallow its own failure.
    "run_step_continue_on_error": [
        s.get("id") or s.get("name") for s in all_steps
        if "run" in s and bool(s.get("continue-on-error"))
    ],
    # The mint is owner-scoped: `owner:` set and `repositories:` deliberately ABSENT, so the mint
    # succeeds whatever the repo selection is and the ENUMERATION is the assertion. A repo-scoped mint
    # would just fail for a de-scoped repo — an ambiguous signal (bad key? wrong id? clock skew?) that
    # cannot distinguish "not in the installation" from "the App is broken".
    "mint_with": {
        k: str(v) for k, v in (
            next((s for s in all_steps if str(s.get("uses", "")).startswith("actions/create-github-app-token")), {})
            .get("with") or {}
        ).items()
    },
    # sync must not be able to run without it.
    "sync_needs": [needs] if isinstance(needs, str) else list(needs or []),
    # the two copies of the "would this actually sync?" condition, whitespace-normalised.
    "sync_if": " ".join(str(sync.get("if", "")).split()),
    "would_sync_env": " ".join(str(env.get("WOULD_SYNC", "")).split()),
    # the env the step actually receives — used to prove secret VALUES never enter it.
    "env": {k: str(v) for k, v in env.items()},
}
with open(meta_out, "w", encoding="utf-8") as fh:
    json.dump(meta, fh)
PY

jq_meta() { python3 -c "import json,sys;print(json.load(open('$META'))$1)"; }

# run_preflight <has_id> <has_key> <would_sync> <same_repo> -> writes combined output, echoes exit code
# GITHUB_OUTPUT is real here: the step publishes `can_mint`, which is what gates the prerequisite-1
# steps. Leaving it unset would make `>> "$GITHUB_OUTPUT"` an ambiguous redirect under `set -eu` and
# every leg below would fail for that reason instead of the one it is testing.
run_preflight() {
  : > "$WORK/step_output"
  HAS_APP_ID="$1" HAS_APP_KEY="$2" WOULD_SYNC="$3" SAME_REPO="$4" \
  GITHUB_REPOSITORY="FS-GG/FS.GG.Game" GITHUB_OUTPUT="$WORK/step_output" \
    bash "$SCRIPT" >"$WORK/out" 2>&1 && echo 0 || echo $?
}

# can_mint <has_id> <has_key> <same_repo> -> the value the step published
can_mint() {
  run_preflight "$1" "$2" 'false' "$3" >/dev/null
  sed -n 's/^can_mint=//p' "$WORK/step_output" | tail -1
}

# ---- prerequisite 1: the installation leg (.github#522) -----------------------------------------
# `gh` is STUBBED, not called: this fixture has no network by design. The stub reproduces exactly the
# contract the step depends on — `gh api /installation/repositories --paginate --jq …` prints one
# full_name per line, or fails non-zero — so the leg tests the step's LOGIC against a real-shaped
# answer, and a change to that contract shows up as a stub that no longer matches the call.
STUB="$WORK/bin"; mkdir -p "$STUB"
make_gh() {   # make_gh <exit-code> [line…]
  local rc="$1"; shift
  { echo '#!/usr/bin/env bash'
    for l in "$@"; do printf 'echo %q\n' "$l"; done
    [ "$rc" = 0 ] || echo 'echo "HTTP 403: Resource not accessible by integration" >&2'
    echo "exit $rc"
  } > "$STUB/gh"
  chmod +x "$STUB/gh"
}

# run_install <token> <would_sync> <gh-exit> [repos…] -> writes output, echoes exit code
run_install() {
  local tok="$1" ws="$2" rc="$3"; shift 3
  make_gh "$rc" "$@"
  PATH="$STUB:$PATH" GH_TOKEN="$tok" WOULD_SYNC="$ws" GITHUB_REPOSITORY="FS-GG/FS.GG.Game" \
    bash "$SCRIPT.install" >"$WORK/out" 2>&1 && echo 0 || echo $?
}

i_green() {  # <name> <token> <would_sync> <gh-rc> <repos…>
  local name="$1"; shift; local rc; rc="$(run_install "$@")"
  if [ "$rc" != 0 ]; then bad "$name" "$(printf 'expected exit 0, got %s\n%s' "$rc" "$(cat "$WORK/out")")"; return; fi
  if grep -q '::error\|::warning' "$WORK/out"; then
    bad "$name" "an installed repo must emit no annotation:$(printf '\n%s' "$(cat "$WORK/out")")"; return; fi
  ok "$name"
}
i_red() {    # <name> <pattern> <token> <would_sync> <gh-rc> <repos…>
  local name="$1" pat="$2"; shift 2; local rc; rc="$(run_install "$@")"
  if [ "$rc" = 0 ]; then bad "$name" "expected a RED exit, got 0 — the gate passed a repo that cannot push."; return; fi
  grep -q '::error' "$WORK/out" || { bad "$name" "exited non-zero but emitted no ::error annotation"; return; }
  grep -qi -- "$pat" "$WORK/out" && ok "$name" \
    || bad "$name" "$(printf 'red, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}
i_amber() {  # <name> <pattern> <token> <would_sync> <gh-rc> <repos…>
  local name="$1" pat="$2"; shift 2; local rc; rc="$(run_install "$@")"
  if [ "$rc" != 0 ]; then bad "$name" "$(printf 'expected a non-blocking exit 0, got %s — must not red an unrelated PR\n%s' "$rc" "$(cat "$WORK/out")")"; return; fi
  grep -q '::warning' "$WORK/out" || { bad "$name" "passed SILENTLY — an un-installed repo must still be visible"; return; }
  grep -q '::error' "$WORK/out" && { bad "$name" "emitted ::error on a non-syncing PR (wrong severity)"; return; }
  grep -qi -- "$pat" "$WORK/out" && ok "$name" \
    || bad "$name" "$(printf 'warned, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

# green <name> <has_id> <has_key> <would_sync>   — must exit 0 and emit NO annotation at all.
green() {
  local name="$1" rc; rc="$(run_preflight "$2" "$3" "$4" "$5")"
  if [ "$rc" != 0 ]; then
    bad "$name" "$(printf 'expected exit 0, got %s\n%s' "$rc" "$(cat "$WORK/out")")"; return
  fi
  if grep -q '::error\|::warning' "$WORK/out"; then
    bad "$name" "a provisioned repo must emit no annotation:$(printf '\n%s' "$(cat "$WORK/out")")"; return
  fi
  ok "$name"
}

# red <name> <has_id> <has_key> <would_sync> <pattern>  — must exit 1 AND say why.
red() {
  local name="$1" pat="$6" rc; rc="$(run_preflight "$2" "$3" "$4" "$5")"
  if [ "$rc" = 0 ]; then
    bad "$name" "expected a RED exit, got 0 — the gate passed a repo that cannot authenticate."; return
  fi
  grep -q '::error' "$WORK/out" || { bad "$name" "exited non-zero but emitted no ::error annotation"; return; }
  grep -qi -- "$pat" "$WORK/out" \
    && ok "$name" \
    || bad "$name" "$(printf 'red, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

# amber <name> <has_id> <has_key> <would_sync> <pattern> — must exit 0 but WARN, loudly and by name.
amber() {
  local name="$1" pat="$6" rc; rc="$(run_preflight "$2" "$3" "$4" "$5")"
  if [ "$rc" != 0 ]; then
    bad "$name" "$(printf 'expected a non-blocking exit 0, got %s — this must not red an unrelated PR\n%s' "$rc" "$(cat "$WORK/out")")"; return
  fi
  grep -q '::warning' "$WORK/out" || { bad "$name" "passed SILENTLY — an unprovisioned repo must still be visible"; return; }
  grep -q '::error' "$WORK/out" && { bad "$name" "emitted ::error on a non-syncing PR (wrong severity)"; return; }
  grep -qi -- "$pat" "$WORK/out" \
    && ok "$name" \
    || bad "$name" "$(printf 'warned, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

# silent_says <name> <has_id> <has_key> <would_sync> <same_repo> <pattern> — exit 0, NO annotation,
# but it must still SAY why it declined to evaluate. Passing mutely and passing for a stated reason
# look identical to CI and completely different to a human reading the log.
silent_says() {
  local name="$1" pat="$6" rc; rc="$(run_preflight "$2" "$3" "$4" "$5")"
  if [ "$rc" != 0 ]; then
    bad "$name" "$(printf 'expected exit 0, got %s\n%s' "$rc" "$(cat "$WORK/out")")"; return
  fi
  if grep -q '::error\|::warning' "$WORK/out"; then
    bad "$name" "a fork PR carries no information about provisioning — it must not annotate at all"; return
  fi
  grep -qi -- "$pat" "$WORK/out" \
    && ok "$name" \
    || bad "$name" "$(printf 'silent, but it never explains itself (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

echo "== behaviour: the truth table (has_id x has_key x would_sync x same_repo) =="

# Provisioned. The only combinations that may pass silently.
green "B1  provisioned + renovate PR      -> green, silent"       true  true  true  true
green "B2  provisioned + ordinary PR      -> green, silent"       true  true  false true

# Unprovisioned AND about to sync. The run was going to fail anyway; preflight only decides whether
# it fails LEGIBLY. These are the legs that replace the opaque create-github-app-token error.
red   "B3  no secrets at all + renovate   -> RED, names both"     false false true  true 'FSGG_DISPATCH_APP_ID'
red   "B4  no secrets at all + renovate   -> RED, names the key"  false false true  true 'FSGG_DISPATCH_APP_PRIVATE_KEY'
red   "B5  app-id only + renovate         -> RED (key missing)"   true  false true  true 'FSGG_DISPATCH_APP_PRIVATE_KEY'
red   "B6  key only + renovate            -> RED (id missing)"    false true  true  true 'FSGG_DISPATCH_APP_ID'
red   "B7  RED names the repo, not just the secret"               false false true  true 'FS-GG/FS.GG.Game'
red   "B8  RED points at the org-admin fix (#468)"                false false true  true '468'

# Unprovisioned, NOT about to sync — FS.GG.Game and FS.GG.Audio's state today on an ordinary PR.
# Must be visible and must NOT block: redding every unrelated PR in a repo over a latent problem is
# how a gate gets switched off, and a gate that is switched off is worth less than no gate at all.
amber "B9  unprovisioned + ordinary PR    -> WARN, non-blocking"  false false false true 'FSGG_DISPATCH_APP_ID'
amber "B10 the warning says it WILL fail later, not that it is fine" false false false true 'next renovate'
amber "B11 half-provisioned + ordinary PR -> WARN, non-blocking"  true  false false true 'FSGG_DISPATCH_APP_PRIVATE_KEY'

# FORK HEADS. GitHub withholds every secret but GITHUB_TOKEN from a workflow triggered from a fork,
# so HAS_APP_ID/HAS_APP_KEY are false on a fork PR in EVERY repo — including the four that are
# demonstrably provisioned. Without the fork guard, preflight annotates a fork PR against
# FS.GG.Rendering (4 green pushes) with "is not provisioned ... it will fail the first renovate/* PR
# that opens", and every clause of that is false. A gate that cries wolf gets switched off, so these
# legs assert TOTAL SILENCE — no ::warning, no ::error — and that it says WHY rather than passing mutely.
#
# Not a fail-open: `sync` requires head.repo == this repo, so on a fork head there is no push to gate.
green  "B12 fork PR, secrets redacted     -> SILENT (no false alarm)"   false false false false
green  "B13 fork + renovate branch        -> SILENT (guard precedes RED)" false false true  false
silent_says "B14 the fork leg explains WHY it did not evaluate"         false false false false 'fork'
silent_says "B15 the fork leg names secret withholding, not provisioning" false false false false 'withholds secrets'

echo
echo "== structure: the gate cannot be skipped into green (the epic #266 class) =="

# S1. An `if:` on preflight would let a future edit skip the gate — and a skipped job is GREEN.
if [ "$(jq_meta "['preflight_has_if']")" = "False" ]; then
  ok "S1  preflight carries no \`if:\` — it cannot be skipped into green"
else
  bad "S1  preflight carries no \`if:\`" \
      "an \`if:\` was added to the preflight job. A skipped job reports GREEN, so this silently disables the gate for whichever calls the condition excludes. That is exactly the #266 signature."
fi

# S2. Without `needs:`, sync runs regardless and dies at the token step — the gate becomes decorative.
if printf '%s' "$(jq_meta "['sync_needs']")" | grep -q "preflight"; then
  ok "S2  sync still \`needs: preflight\` — the gate cannot be bypassed"
else
  bad "S2  sync still \`needs: preflight\`" \
      "\`needs: preflight\` was removed from the sync job, so sync now runs even when preflight fails. The gate is decorative: an unprovisioned repo goes straight back to the opaque token-step failure."
fi

# S3. Preflight's WOULD_SYNC is a hand-copy of sync's `if:`. It only grades severity (sync's own `if:`
# still enforces), so drift is not a security hole — but it silently misgrades error-vs-warning, and
# nothing else would ever notice.
SYNC_IF="$(jq_meta "['sync_if']")"
WOULD="$(jq_meta "['would_sync_env']")"
WOULD_BARE="$(printf '%s' "$WOULD" | sed -e 's/^\${{ *//' -e 's/ *}}$//')"
if [ -n "$SYNC_IF" ] && [ "$SYNC_IF" = "$WOULD_BARE" ]; then
  ok "S3  preflight's WOULD_SYNC is identical to sync's \`if:\` (no drift)"
else
  bad "S3  preflight's WOULD_SYNC is identical to sync's \`if:\`" \
      "$(printf 'the two copies of the sync condition have drifted, so preflight will misgrade error-vs-warning:\n  sync.if   = %s\n  WOULD_SYNC = %s' "$SYNC_IF" "$WOULD_BARE")"
fi

# S4. The step must receive BOOLEANS, never the secret values. `${{ secrets.app-id }}` in the env
# would put the App id in the environment of a job that has no business holding it — and this job
# runs in every receiver, on every PR, including ones from an unprovisioned repo.
LEAK="$(python3 - "$META" <<'PY'
import json, re, sys
env = json.load(open(sys.argv[1]))["env"]
# A bare `secrets.x` reference is a value; `secrets.x != ''` is a boolean. Only the latter is allowed.
bad = [f"{k}={v}" for k, v in env.items()
       if re.search(r"secrets\.[A-Za-z0-9_.-]+\s*}}", v) and "!=" not in v]
print("; ".join(bad))
PY
)"
if [ -z "$LEAK" ]; then
  ok "S4  preflight's env carries booleans only — no secret value reaches the step"
else
  bad "S4  preflight's env carries booleans only" \
      "a raw secret value was placed in the preflight step's environment: $LEAK. Pass \`secrets.x != ''\` instead — Actions evaluates it before the step runs, so the job sees only \"true\"/\"false\"."
fi

# S5. `continue-on-error: true` makes a FAILING job report SUCCESS. It is the single cheapest way to
# turn this gate off while leaving every line of it in place — and the one a future "just get CI
# green" commit reaches for first. Two sibling gates warn about it in a comment; a comment is not a
# gate, which is the whole thesis of epic #266.
if [ "$(jq_meta "['preflight_continue_on_error']")" = "False" ]; then
  ok "S5  the preflight JOB carries no \`continue-on-error\` — a red gate stays red"
else
  bad "S5  the preflight JOB carries no \`continue-on-error\`" \
      "\`continue-on-error: true\` was added to the preflight job. A FAILING gate now reports SUCCESS, sync runs anyway, and an unprovisioned repo goes straight back to the opaque token-step failure — with the gate still sitting there looking like it works."
fi

# S6. No VERDICT-RENDERING step may swallow its own failure. The token mint (a `uses:` step) is
# allowed to — it hands control to `installation`, which grades the severity itself (S7/T-legs prove
# it fails closed on the empty token that produces). But a `run:` step IS the verdict, and one that
# swallows its own exit is the same gate-off switch S5 guards, moved down one level.
CoE_RUN="$(jq_meta "['run_step_continue_on_error']")"
if [ "$CoE_RUN" = "[]" ]; then
  ok "S6  no \`run:\` step in preflight carries \`continue-on-error\` — the verdict cannot be swallowed"
else
  bad "S6  no \`run:\` step in preflight carries \`continue-on-error\`" \
      "these verdict steps swallow their own failure: $CoE_RUN. A \`run:\` step IS the gate's answer; \`continue-on-error\` there makes a red one report green."
fi

# S7. The mint MUST be owner-scoped: `owner:` set, `repositories:` absent. A repo-scoped mint simply
# FAILS for a de-scoped repo, and a failed mint cannot distinguish "not in the installation" from
# "the App is broken" — so the gate would report the right verdict for the wrong reason, or the wrong
# one. Enumerating the installation is the assertion; the mint only has to succeed.
MINT_OWNER="$(python3 -c "import json;w=json.load(open('$META'))['mint_with'];print(w.get('owner',''))")"
MINT_REPOS="$(python3 -c "import json;w=json.load(open('$META'))['mint_with'];print(w.get('repositories','') or '')")"
if [ -n "$MINT_OWNER" ] && [ -z "$MINT_REPOS" ]; then
  ok "S7  the preflight mint is owner-scoped (\`owner:\` set, no \`repositories:\`) — it succeeds whatever the selection is"
else
  bad "S7  the preflight mint is owner-scoped" \
      "$(printf 'owner=%q repositories=%q — a repo-scoped mint FAILS for a de-scoped repo, which is the very thing this leg is trying to detect. It cannot tell "not installed" from "App broken".' "$MINT_OWNER" "$MINT_REPOS")"
fi

# --- can_mint: prerequisite 1 must only be judged where a mint is meaningful ----------------------
[ "$(can_mint true true true)"   = 'true'  ] && ok "C1  can_mint=true when both secrets resolve — prerequisite 1 is checkable" \
  || bad "C1  can_mint=true when both secrets resolve" "prerequisite 1 would never run"
[ "$(can_mint false true true)"  = 'false' ] && ok "C2  can_mint=false when a secret is missing — a failed mint would blame the App for a secrets gap" \
  || bad "C2  can_mint=false when a secret is missing" "the installation check would run and misattribute the cause"
[ "$(can_mint true true false)"  = 'false' ] && ok "C3  can_mint=false on a fork — no secrets by design, so nothing to mint with" \
  || bad "C3  can_mint=false on a fork" "the installation check would run against empty secrets on every fork PR"

# --- prerequisite 1 truth table ------------------------------------------------------------------
IN='FS-GG/FS.GG.Game'; OTHER='FS-GG/FS.GG.Rendering'
i_green "T1  installed + would sync            -> green, silent"        tok true  0 "$OTHER" "$IN"
i_green "T2  installed + unrelated PR          -> green, silent"        tok false 0 "$IN"
i_red   "T3  NOT installed + renovate/* PR     -> RED, names the App"   'App installation' tok true  0 "$OTHER"
i_amber "T4  NOT installed + unrelated PR      -> amber, does not block" 'App installation' tok false 0 "$OTHER"

# The leg that makes the mint's `continue-on-error` safe (S6). A swallowed mint failure arrives here
# as an EMPTY token — and must not read as "installed". This is the fail-open the whole design turns on.
i_red   "T5  empty token (mint failed) + renovate/* PR -> RED, not a silent pass" 'would not mint' '' true  0 "$IN"
i_amber "T6  empty token (mint failed) + unrelated PR  -> amber, not a silent pass" 'would not mint' '' false 0 "$IN"

# An API failure is a NO-VERDICT, not a pass. "I could not ask" and "I asked, and it is fine" must not
# produce the same exit code — epic #266's ratified rule, applied to this leg.
i_amber "T7  the enumeration API fails         -> amber no-verdict, never a green pass" 'could not verify' tok false 1 ""

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1
