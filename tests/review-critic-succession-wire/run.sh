#!/usr/bin/env bash
# Wire-boundary fixture for `ReviewApplication.fs`'s `criticSuccessionGranted` JSON parsing (.github#2417).
#
# `tests/FS.GG.Coord.Cli.Tests/ReviewApplicationTests.fs` is the natural home for this coverage, but that
# path is declared by a DIFFERENT live claim (.github#2394) for the duration of this item, and `widen`
# refuses on OVERLAP rather than silently taking it. This fixture exercises the exact same boundary
# hermetically, the same way `tests/coord-engine-e2e` and `tests/recipe-landable` exercise other CLI
# surfaces without living inside the xunit tree: it runs the COMPILED `fsgg-coord-engine review --snapshot`
# — the pure DECISION path, no board, no token, no network — against four crafted snapshots and asserts on
# its JSON stdout and exit code.
#
# Four legs, matching the review comment's request: ABSENT (no `criticSuccessionGranted` key at all — the
# backward-compatibility case every existing snapshot producer relies on), NULL (explicit null, the same
# fact spelled out), VALID (a well-formed grant that the engine must honor end to end, receipt echoed on
# the wire), and MALFORMED (a non-object, non-null value, which must fail CLOSED with exit 1 and a message
# naming the field — never silently ignored, and never a `noVerdict` that could be mistaken for a read
# failure elsewhere in the protocol).
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE_DLL="$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

if [ ! -f "$ENGINE_DLL" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE_DLL)" >&2
  exit 1
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/review-succession-wire.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

binding_and_comments() {
  cat <<'JSON'
  "binding": {
    "itemRef": "FS-GG/.github#2343",
    "pr": 2413,
    "headSha": "head2",
    "claimGeneration": "gen-1",
    "implementerIdentity": "impl-worker",
    "phase": "ordinary",
    "round": 1
  },
JSON
}

comments_field() {
  cat <<'JSON'
    "comments": [
      { "id": 1, "url": "https://reviews/1", "body": "<!-- fsgg:independent-review:v1 -->\ncritic: kite\nreviewed-head: head1\nverdict: changes-required" }
    ],
    "checks": "pending",
    "repairPhaseGranted": null,
    "repairRouteAvailable": true
JSON
}

run_engine() {
  # $1 = snapshot file. Prints stdout; exit code is the caller's to inspect via $?.
  dotnet "$ENGINE_DLL" review --snapshot "$1" --json
}

# ---- 1. ABSENT: no `criticSuccessionGranted` key in `facts` at all ------------------------------------
ABSENT="$WORK/absent.json"
{
  echo "{"
  binding_and_comments
  echo '  "facts": {'
  comments_field
  echo "  }"
  echo "}"
} > "$ABSENT"

out="$(run_engine "$ABSENT")"; rc=$?
if [ "$rc" -eq 0 ]; then
  action="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])' 2>/dev/null)"
  receipt="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["criticSuccessionReceipt"])' 2>/dev/null)"
  reason="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["actionReason"])' 2>/dev/null)"
  if [ "$action" = "resumeSameCritic" ] && [ "$receipt" = "None" ]; then
    if printf '%s' "$reason" | grep -q "refused, not consumed"; then
      bad "ABSENT: the pre-#2417 reason text must be UNCHANGED (no grant to refuse)" "$reason"
    else
      ok "ABSENT: no key at all parses as no grant -- resumeSameCritic, unchanged reason, no receipt echoed"
    fi
  else
    bad "ABSENT: expected action=resumeSameCritic and no receipt" "$out"
  fi
else
  bad "ABSENT: the absent case must be exit 0 (backward compatible), got $rc" "$out"
fi

# ---- 2. NULL: `criticSuccessionGranted` explicitly present as JSON null -------------------------------
NULLED="$WORK/nulled.json"
{
  echo "{"
  binding_and_comments
  echo '  "facts": {'
  comments_field
  echo ','
  echo '    "criticSuccessionGranted": null'
  echo "  }"
  echo "}"
} > "$NULLED"

out="$(run_engine "$NULLED")"; rc=$?
if [ "$rc" -eq 0 ]; then
  action="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])' 2>/dev/null)"
  if [ "$action" = "resumeSameCritic" ]; then
    ok "NULL: an explicit null parses identically to an absent key -- resumeSameCritic"
  else
    bad "NULL: expected action=resumeSameCritic" "$out"
  fi
else
  bad "NULL: an explicit null must be exit 0, got $rc" "$out"
fi

# ---- 3. VALID: a well-formed, matching grant is honored end to end ------------------------------------
VALID="$WORK/valid.json"
{
  echo "{"
  binding_and_comments
  echo '  "facts": {'
  comments_field
  echo ','
  cat <<'JSON'
    "criticSuccessionGranted": {
      "originalCriticIdentity": "kite",
      "successorCriticIdentity": "fresh-critic",
      "grantedBy": "host-9b63",
      "reason": "the original critic despawned before confirming the new commit",
      "candidateHeadSha": "head2"
    }
JSON
  echo "  }"
  echo "}"
} > "$VALID"

out="$(run_engine "$VALID")"; rc=$?
if [ "$rc" -eq 0 ]; then
  action="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])' 2>/dev/null)"
  successor="$(printf '%s' "$out" | python3 -c 'import json,sys; print(json.load(sys.stdin)["criticSuccessionReceipt"]["successorCriticIdentity"])' 2>/dev/null)"
  if [ "$action" = "enterCriticSuccession" ] && [ "$successor" = "fresh-critic" ]; then
    ok "VALID: a matching, well-formed grant yields enterCriticSuccession with the receipt echoed on the wire"
  else
    bad "VALID: expected action=enterCriticSuccession with successorCriticIdentity=fresh-critic" "$out"
  fi
else
  bad "VALID: a well-formed grant must be exit 0" "$out"
fi

# ---- 4. MALFORMED: a non-object, non-null value fails CLOSED -------------------------------------------
MALFORMED="$WORK/malformed.json"
{
  echo "{"
  binding_and_comments
  echo '  "facts": {'
  comments_field
  echo ','
  echo '    "criticSuccessionGranted": "not-an-object"'
  echo "  }"
  echo "}"
} > "$MALFORMED"

out="$(run_engine "$MALFORMED" 2>&1)"; rc=$?
if [ "$rc" -ne 0 ]; then
  if printf '%s' "$out" | grep -q "criticSuccessionGranted"; then
    ok "MALFORMED: a non-object value fails CLOSED (exit $rc) and names the field"
  else
    bad "MALFORMED: failed closed but did not name the field -- a caller cannot tell which key is bad" "$out"
  fi
else
  bad "GATE INVERSION: a malformed criticSuccessionGranted value was silently accepted (exit 0)" "$out"
fi

echo
echo "review-critic-succession-wire fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
