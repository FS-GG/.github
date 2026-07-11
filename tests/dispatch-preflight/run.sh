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
# it cannot prove the script still RUNS. Legs S1-S4 assert the gate cannot be skipped into green:
# preflight carries no `if:`, `sync` still `needs:` it, the two copies of the sync condition have not
# drifted, and the secret VALUES never reach the environment.
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
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

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

steps = [s for s in (pre.get("steps") or []) if "run" in s]
if len(steps) != 1:
    sys.exit(f"expected exactly one `run:` step in preflight, found {len(steps)}.")
step = steps[0]

with open(script_out, "w", encoding="utf-8") as fh:
    fh.write(step["run"])

env = step.get("env") or {}
needs = sync.get("needs")
meta = {
    # `if:` on the gate itself would let it be skipped into green — the #266 signature.
    "preflight_has_if": "if" in pre,
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

# run_preflight <has_id> <has_key> <would_sync> -> writes combined output, echoes exit code
run_preflight() {
  HAS_APP_ID="$1" HAS_APP_KEY="$2" WOULD_SYNC="$3" \
  GITHUB_REPOSITORY="FS-GG/FS.GG.Game" \
    bash "$SCRIPT" >"$WORK/out" 2>&1 && echo 0 || echo $?
}

# green <name> <has_id> <has_key> <would_sync>   — must exit 0 and emit NO annotation at all.
green() {
  local name="$1" rc; rc="$(run_preflight "$2" "$3" "$4")"
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
  local name="$1" pat="$5" rc; rc="$(run_preflight "$2" "$3" "$4")"
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
  local name="$1" pat="$5" rc; rc="$(run_preflight "$2" "$3" "$4")"
  if [ "$rc" != 0 ]; then
    bad "$name" "$(printf 'expected a non-blocking exit 0, got %s — this must not red an unrelated PR\n%s' "$rc" "$(cat "$WORK/out")")"; return
  fi
  grep -q '::warning' "$WORK/out" || { bad "$name" "passed SILENTLY — an unprovisioned repo must still be visible"; return; }
  grep -q '::error' "$WORK/out" && { bad "$name" "emitted ::error on a non-syncing PR (wrong severity)"; return; }
  grep -qi -- "$pat" "$WORK/out" \
    && ok "$name" \
    || bad "$name" "$(printf 'warned, but not for the stated reason (want %q):\n%s' "$pat" "$(cat "$WORK/out")")"
}

echo "== behaviour: the full truth table (has_id x has_key x would_sync) =="

# Provisioned. The only combinations that may pass silently.
green "B1  provisioned + renovate PR      -> green, silent"       true  true  true
green "B2  provisioned + ordinary PR      -> green, silent"       true  true  false

# Unprovisioned AND about to sync. The run was going to fail anyway; preflight only decides whether
# it fails LEGIBLY. These are the legs that replace the opaque create-github-app-token error.
red   "B3  no secrets at all + renovate   -> RED, names both"     false false true  'FSGG_DISPATCH_APP_ID'
red   "B4  no secrets at all + renovate   -> RED, names the key"  false false true  'FSGG_DISPATCH_APP_PRIVATE_KEY'
red   "B5  app-id only + renovate         -> RED (key missing)"   true  false true  'FSGG_DISPATCH_APP_PRIVATE_KEY'
red   "B6  key only + renovate            -> RED (id missing)"    false true  true  'FSGG_DISPATCH_APP_ID'
red   "B7  RED names the repo, not just the secret"               false false true  'FS-GG/FS.GG.Game'
red   "B8  RED points at the org-admin fix (#468)"                false false true  '468'

# Unprovisioned, NOT about to sync — FS.GG.Game and FS.GG.Audio's state today on an ordinary PR.
# Must be visible and must NOT block: redding every unrelated PR in a repo over a latent problem is
# how a gate gets switched off, and a gate that is switched off is worth less than no gate at all.
amber "B9  unprovisioned + ordinary PR    -> WARN, non-blocking"  false false false 'FSGG_DISPATCH_APP_ID'
amber "B10 the warning says it WILL fail later, not that it is fine" false false false 'next renovate'
amber "B11 half-provisioned + ordinary PR -> WARN, non-blocking"  true  false false 'FSGG_DISPATCH_APP_PRIVATE_KEY'

# A fork PR on a renovate/* branch: `sync` refuses it (never auto-commit to an untrusted head), so
# WOULD_SYNC is false and this must warn, never red. The security boundary is unchanged by preflight.
amber "B12 fork renovate PR               -> WARN, never RED"     false false false 'FSGG_DISPATCH_APP_ID'

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

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1
