#!/usr/bin/env bash
# Fixture for scripts/check-claim-generation.py (.github#2342, slice 1 of .github#1858 step 2).
#
# Offline. `gh` is stubbed on PATH and fails LIKE THE REAL ONE — a 404 is a real answer ("this issue
# does not exist"), a 403 is a permission a human must grant, and a rate limit is neither. Same idiom
# as tests/required-contexts/run.sh, over a different subject: issue COMMENTS instead of branch
# protection, because this gate's live read is "what is the current fsgg:claim winner", not "what
# does branch protection require".
#
# Every negative leg asserts the REASON (a distinctive substring), not merely a non-zero exit —
# tests/feed-coherence/run.sh's own note applies here too: a "must fail" test whose non-zero exit came
# from the wrong guard is a vacuous pass wearing a red badge.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-claim-generation.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/claim-generation-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
unset GITHUB_TOKEN GH_TOKEN FSGG_CLAIM_LEASE_MIN || true

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# ---------------------------------------------------------------------------------------------
# The `gh` stub. Serves a WORLD directory:
#   $WORLD/comments/<owner>__<repo>__<n>.ndjson       one JSON {id,body,updated_at} object per line —
#                                                      exactly the shape `gh api --paginate --jq
#                                                      '.[] | {id,body,updated_at}'` would print.
#   $WORLD/comments/<slug>.forbidden                  403 — the token may not read this issue's comments
#   $WORLD/comments/<slug>.unreachable                rate limit / outage — never a verdict
#   $WORLD/pulls.ndjson                             one {number,body,ref,sha} object per line — the
#                                                      post-`--jq` shape `--sweep`'s PR listing reads.
#   $WORLD/checkruns/<sha>.ndjson                     one {id,conclusion,started_at} per line — the
#                                                      STANDING `claim-generation` verdict(s) on <sha>.
#                                                      An ABSENT file is "no standing check run", which
#                                                      the sweep must leave alone (it never originates).
#   $WORLD/checkruns/<sha>.forbidden                  403 on reading that head's check runs.
#   $WORLD/posted.ndjson                              WRITTEN BY THE STUB: every check run the sweep
#                                                      published, so a leg can assert exactly what it did.
#
# An ABSENT comments file is a 404 — this item does not exist. That is deliberately different from a
# PRESENT, empty (or marker-free) file, which is a real "this item exists and nobody holds it" answer
# (the engine's own `released` sentinel). Collapsing the two would make "wrong item number" and
# "correct item, currently unclaimed" indistinguishable, which is exactly the pair `check-claim-
# generation.py`'s `Missing` vs. `RELEASED` handling is written to keep apart.
#
# The stub serves the ALREADY-`--jq`'d shape rather than the raw API envelope, as the comments case
# above already does. That is a deliberate fixture concession, named here so nobody mistakes it for
# fidelity: it exercises this gate's parsing of the lines it receives, not `gh`'s own `--jq`.
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
path=""; method="GET"
for a in "$@"; do
  case "$a" in
    repos/*) path="$a";;
    POST)    method="POST";;
  esac
done

notfound()  { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
forbidden() { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
apifail()   { echo "gh: API rate limit exceeded for installation (HTTP 500)" >&2; exit 1; }

base="${path%%\?*}"
rest="${base#repos/}"
case "$method:$rest" in
  POST:*/check-runs)
    mkdir -p "$WORLD"
    cat >> "$WORLD/posted.ndjson"
    printf '\n' >> "$WORLD/posted.ndjson"
    echo '{"id": 1}'
    ;;
  GET:*/issues/*/comments)
    repo="${rest%%/issues/*}"; num="${rest#*/issues/}"; num="${num%/comments}"
    slug="${repo//\//__}__${num}"
    [ -e "$WORLD/comments/$slug.forbidden" ]   && forbidden
    [ -e "$WORLD/comments/$slug.unreachable" ] && apifail
    f="$WORLD/comments/$slug.ndjson"
    [ -f "$f" ] || notfound
    cat "$f"
    ;;
  GET:*/pulls)
    f="$WORLD/pulls.ndjson"
    [ -f "$f" ] || notfound
    cat "$f"
    ;;
  GET:*/commits/*/check-runs)
    sha="${rest#*/commits/}"; sha="${sha%/check-runs}"
    [ -e "$WORLD/checkruns/$sha.forbidden" ] && forbidden
    f="$WORLD/checkruns/$sha.ndjson"
    [ -f "$f" ] && cat "$f"
    exit 0
    ;;
  *) notfound ;;
esac
STUB
chmod +x "$STUB/gh"

# comment <world> <repo> <n> <id> <ago-minutes> [worker]  — append ONE live fsgg:claim marker,
# `ago-minutes` in the past, to repo#n's world. worker defaults to a placeholder; this gate never
# reads it.
comment() {
  local world="$1" repo="$2" num="$3" id="$4" ago="$5" worker="${6:-fixture-worker}"
  mkdir -p "$world/comments"
  local slug="${repo//\//__}__${num}"
  local ts
  ts="$(date -u -d "@$(( $(date +%s) - ago*60 ))" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null \
        || date -u -v-"${ago}"M +"%Y-%m-%dT%H:%M:%SZ")"
  python3 -c "
import json
print(json.dumps({'id': $id, 'body': '<!-- fsgg:claim worker=$worker lease=120 renewed=0 -->', 'updated_at': '$ts'}))
" >> "$world/comments/$slug.ndjson"
}

# comment_at <world> <repo> <n> <id> <iso-updated-at> [lease] [worker]  — the same, at an ABSOLUTE
# instant and with an explicit `lease=`. The lease-expiry legs (`.github#2656`) need both: a claim whose
# staleness is decided by the lease the MARKER declares, and a clock the leg controls rather than
# observes, so that "green at T, red at T+lease, nothing else changed" is a fact and not a race.
# A lease of `none` emits a marker with NO `lease=` field at all, which is a different world from one
# that declares a lease this gate happens to agree with: it is the only way to exercise the fallback.
comment_at() {
  local world="$1" repo="$2" num="$3" id="$4" ts="$5" lease="${6:-120}" worker="${7:-fixture-worker}"
  mkdir -p "$world/comments"
  local slug="${repo//\//__}__${num}" leasepart=" lease=$lease"
  [ "$lease" = "none" ] && leasepart=""
  python3 -c "
import json
print(json.dumps({'id': $id, 'body': '<!-- fsgg:claim worker=$worker$leasepart renewed=0 -->', 'updated_at': '$ts'}))
" >> "$world/comments/$slug.ndjson"
}

# pr <world> <number> <ref> <sha> <body>  — one open pull request in the sweep's listing.
pr() {
  local world="$1" number="$2" ref="$3" sha="$4" body="$5"
  mkdir -p "$world"
  BODY="$body" python3 -c "
import json, os
print(json.dumps({'number': $number, 'body': os.environ['BODY'], 'ref': '$ref', 'sha': '$sha'}))
" >> "$world/pulls.ndjson"
}

# checkrun <world> <sha> <conclusion> [started_at] [id]  — the STANDING claim-generation verdict.
checkrun() {
  local world="$1" sha="$2" conclusion="$3" started="${4:-2026-01-01T00:00:00Z}" id="${5:-900}"
  mkdir -p "$world/checkruns"
  python3 -c "
import json
print(json.dumps({'id': $id, 'conclusion': '$conclusion', 'started_at': '$started'}))
" >> "$world/checkruns/$sha.ndjson"
}

# no_claim <world> <repo> <n>  — the item EXISTS (a readable comments payload) but nobody holds it.
no_claim() {
  local world="$1" repo="$2" num="$3"
  mkdir -p "$world/comments"
  : > "$world/comments/${repo//\//__}__${num}.ndjson"
}

# marker <item> <gen> <head> [extra]  — a well-formed fsgg:pr-authorization PR body.
marker() {
  local item="$1" gen="$2" head="$3" extra="${4:-}"
  printf 'Implements the thing.\n\n<!-- fsgg:pr-authorization v=1 item=%s gen=%s head=%s%s -->\n' \
    "$item" "$gen" "$head" "${extra:+ $extra}"
}

HEAD_SHA="$(printf '%040d' 1)"  # a syntactically valid 40-hex sha, arbitrary value

run() {  # run <world> <repo> <head-ref> <head-sha> <body-text> [extra args…]
  local world="$1" repo="$2" ref="$3" sha="$4" body="$5"; shift 5
  printf '%s' "$body" > "$WORK/body.md"
  PATH="$STUB:$PATH" WORLD="$world" FSGG_CLAIM_GEN_TRIES=1 FSGG_CLAIM_GEN_RETRY_DELAY=0 \
    python3 "$TOOL" --repo "$repo" --head-ref "$ref" --head-sha "$sha" --body "$WORK/body.md" "$@" 2>&1
}

expect() {  # expect <name> <want-rc> <needle> <world> <repo> <head-ref> <head-sha> <body-text> [extra…]
  local name="$1" want="$2" needle="$3"; shift 3
  local out rc=0
  out="$(run "$@")" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

REPO="FS-GG/.github"
ITEM="$REPO#2342"
REF="item/2342-fence-merges"

# =============================================================================================
# 0. NOT AN ITEM-DELIVERY BRANCH — vacuous OK, no live read attempted (no world at all).
# =============================================================================================
W0="$WORK/w0"
expect "non-item branch: OK, nothing to fence" \
  0 "not an item-delivery branch" "$W0" "$REPO" "chore/bump-deps" "$HEAD_SHA" ""

# =============================================================================================
# 1. MISSING
# =============================================================================================
W1="$WORK/w1"; comment "$W1" "$REPO" 2342 5000000001 5
expect "missing: no marker at all" \
  1 "[missing]: no \`fsgg:pr-authorization\` marker" "$W1" "$REPO" "$REF" "$HEAD_SHA" \
  "Just fixes a typo, no marker here."

expect "missing: duplicate markers" \
  1 "markers in the PR body" "$W1" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

expect "missing: marker without gen=" \
  1 "missing required field(s): gen" "$W1" "$REPO" "$REF" "$HEAD_SHA" \
  "<!-- fsgg:pr-authorization v=1 item=$ITEM head=$HEAD_SHA -->"

expect "missing: unsupported version" \
  1 "does not understand" "$W1" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA" "" | sed 's/v=1/v=2/')"

# =============================================================================================
# 2. MISMATCHED
# =============================================================================================
W2="$WORK/w2"; comment "$W2" "$REPO" 2342 5000000001 5

expect "mismatched: gen is not numeric" \
  1 "not shaped like a claim-marker comment id" "$W2" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" "not-a-number" "$HEAD_SHA")"

expect "mismatched: head does not match the PR's current head" \
  1 "does not equal this PR's current head SHA" "$W2" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$(printf '%040d' 9)")"

expect "mismatched: item field is malformed" \
  1 "not shaped like \`owner/repo#n\`" "$W2" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "garbage" 5000000001 "$HEAD_SHA")"

expect "mismatched: item field names a different item" \
  1 "does not match $ITEM" "$W2" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$REPO#9999" 5000000001 "$HEAD_SHA")"

W2R="$WORK/w2r"; no_claim "$W2R" "$REPO" 2342
expect "mismatched: item exists but nobody currently holds it (released)" \
  1 "carries no \`fsgg:claim\` marker at all" "$W2R" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

# .github#2656 AC3: this diagnosis must say EVICTION, so that a reader cannot confuse it with the
# EXPIRY case (section 8 below), whose repair is entirely different — `heartbeat`, not re-`take`.
expect "mismatched: the released diagnosis names eviction and points at [expired] for the other case" \
  1 "This is EVICTION, not expiry" "$W2R" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

W2M="$WORK/w2m"
expect "mismatched: the named item does not exist at all (404)" \
  1 "does not exist" "$W2M" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

# =============================================================================================
# 3. STALE — the item IS live-claimed, under a DIFFERENT generation than the PR names.
# =============================================================================================
W3="$WORK/w3"; comment "$W3" "$REPO" 2342 5000000099 5
expect "stale: claim moved on since this PR was authorized" \
  1 "currently held under claim generation 5000000099" "$W3" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

# The naive "compare the HIGHEST id" bug: two LIVE markers, lowest wins. Naming the higher
# (non-winning) one must still read as stale, not as current.
W3B="$WORK/w3b"; comment "$W3B" "$REPO" 2342 5000000150 5; comment "$W3B" "$REPO" 2342 5000000300 5
expect "stale: lowest LIVE id wins, not the highest" \
  1 "currently held under claim generation 5000000150" "$W3B" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000300 "$HEAD_SHA")"

# =============================================================================================
# 4. UNREADABLE
# =============================================================================================
W4="$WORK/w4"; mkdir -p "$W4/comments"; : > "$W4/comments/${REPO//\//__}__2342.forbidden"
expect "unreadable: forbidden (permanent, exit 3)" \
  3 "cannot read" "$W4" "$REPO" "$REF" "$HEAD_SHA" "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

W4B="$WORK/w4b"; mkdir -p "$W4B/comments"; : > "$W4B/comments/${REPO//\//__}__2342.unreachable"
expect "unreadable: rate limit / outage (retryable, exit 2)" \
  2 "no verdict" "$W4B" "$REPO" "$REF" "$HEAD_SHA" "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

# =============================================================================================
# 5. THE PASSING CASE — and the trap a naive "any live marker will do" implementation would miss.
# =============================================================================================
W5="$WORK/w5"; comment "$W5" "$REPO" 2342 5000000001 5
expect "pass: authorization names the current live generation" \
  0 "OK" "$W5" "$REPO" "$REF" "$HEAD_SHA" "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"

# A STALE marker (way past the 120-minute default lease) sits alongside the live one, at a LOWER id.
# A naive "lowest id in the comments list" implementation would pick it and wrongly report `stale`.
# The real CAS excludes lapsed leases, so the live winner is still the fresh one.
W5B="$WORK/w5b"
comment "$W5B" "$REPO" 2342 5000000001 300   # 300 minutes old — stale under the 120-minute default
comment "$W5B" "$REPO" 2342 5000000099 5     # fresh
expect "pass: an earlier STALE marker does not steal the win from the live one" \
  0 "OK" "$W5B" "$REPO" "$REF" "$HEAD_SHA" "$(marker "$ITEM" 5000000099 "$HEAD_SHA")"

# Extra, unknown fields (a future opkey=/grant=) must not be rejected — forward compatibility with
# design slice 3, which will add them to the SAME marker (see the tool's own docstring).
W5C="$WORK/w5c"; comment "$W5C" "$REPO" 2342 5000000001 5
expect "pass: unknown extra fields (opkey=/grant=) are tolerated" \
  0 "OK" "$W5C" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA" "opkey=deadbeef grant=42")"

# =============================================================================================
# 6. --lease-minutes actually changes the staleness boundary (proves the knob is wired, not decorative)
# =============================================================================================
W6="$WORK/w6"; comment "$W6" "$REPO" 2342 5000000001 50  # 50 minutes old
expect "lease: 50-minute-old marker is LIVE under the 120-minute default" \
  0 "OK" "$W6" "$REPO" "$REF" "$HEAD_SHA" "$(marker "$ITEM" 5000000001 "$HEAD_SHA")"
expect "lease: the SAME marker is EXPIRED under a tightened 30-minute lease" \
  1 "claim LEASE HAS LAPSED" "$W6" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")" --lease-minutes 30
# ...and an explicit --lease-minutes OUTRANKS the marker's own `lease=120` (`.github#2656` AC2's
# precedence: an operator tightening the window in CI must not be opt-out-able by a marker).
expect "lease: --lease-minutes outranks the marker's own lease= and is the one named in the diagnosis" \
  1 "its 30-minute lease (set by --lease-minutes)" "$W6" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")" --lease-minutes 30

# =============================================================================================
# 7. .github#2488 — THE FIVE-FOR-FIVE SCENARIO, NAMED. A freshly opened item/<n>-* PR, exactly as
# GitHub's `opened` event would present it BEFORE a worker's own live, non-`--apply` `delivery <ref>
# --pr N` status read (run from a shell that holds a working board credential — round 1 repair: this is
# NOT a CI-side self-heal, which cannot run under this org's CI credential inventory; see
# `coherence.yml`'s `claim-generation` job comment) has had a chance to reach it: a real live claim
# exists, but the PR body carries no `fsgg:pr-authorization` marker at all — reds. The SAME item, SAME
# live claim, and the marker `Client.ensureAuthorization`/`Client.authorizationMarker`
# (`src/FS.GG.Coord.Cli/Client.fs`) writes once that call reaches it — greens. These two legs are cases
# 1 and 5 above, replayed against THIS item's own ref rather than `#2342`'s, so a reader auditing
# #2488's own acceptance criterion 4 finds the exact scenario by number instead of having to infer that
# the pre-existing legs already cover it.
# =============================================================================================
ITEM_2488="$REPO#2488"
REF_2488="item/2488-authorization-emission-timing"
W7="$WORK/w7"; comment "$W7" "$REPO" 2488 5274354824 5

expect ".github#2488: a freshly opened item PR with no marker reds, exactly as measured" \
  1 "[missing]: no \`fsgg:pr-authorization\` marker" "$W7" "$REPO" "$REF_2488" "$HEAD_SHA" \
  "Fixes the emission timing. Closes #2488."

expect ".github#2488: the SAME PR greens once the emission (ensureAuthorization) has written the marker" \
  0 "OK" "$W7" "$REPO" "$REF_2488" "$HEAD_SHA" \
  "$(marker "$ITEM_2488" 5274354824 "$HEAD_SHA")"

# =============================================================================================
# 8. .github#2656 AC2 — THE LEASE IS THE MARKER'S OWN, NOT THIS GATE'S. Each `fsgg:claim` marker
# carries `lease=<minutes>` (`Writes.claimMarker`). A gate that measured every marker against its own
# built-in 120 would declare a worker who took an item with a SHORTER lease still live long after it
# lapsed, and one who took it with a LONGER lease dead while it is held. Both directions are checked,
# against an absolute clock so neither leg can pass by accident of when the fixture ran.
# =============================================================================================
T0="2026-06-01T00:00:00Z"
at() { python3 -c "
from datetime import datetime, timedelta, timezone
print((datetime(2026,6,1,tzinfo=timezone.utc) + timedelta(minutes=$1)).strftime('%Y-%m-%dT%H:%M:%SZ'))
"; }

W8A="$WORK/w8a"; comment_at "$W8A" "$REPO" 2656 5300000001 "$T0" 30
expect "marker lease: a 30-minute lease is EXPIRED at T+45 even though this gate's default is 120" \
  1 "its 30-minute lease (declared by that marker)" "$W8A" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" \
  "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 45)"

W8B="$WORK/w8b"; comment_at "$W8B" "$REPO" 2656 5300000001 "$T0" 240
expect "marker lease: a 240-minute lease is still LIVE at T+180 even though this gate's default is 120" \
  0 "OK" "$W8B" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" \
  "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 180)"

W8C="$WORK/w8c"; comment_at "$W8C" "$REPO" 2656 5300000001 "$T0" none  # no lease= field at all
expect "marker lease: a marker with no lease= falls back, and SAYS it fell back" \
  1 "this gate's fallback — the marker declares no \`lease=\`" "$W8C" "$REPO" "item/2656-lease-expiry" \
  "$HEAD_SHA" "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 121)"

# =============================================================================================
# 9. .github#2656 AC4 — THE ORDERING THAT WAS NEVER EXERCISED. `osprey-174c`'s near-miss was caught
# only because the lease lapsed BEFORE `delivery`, so the authorization was stale on arrival. The
# ordering that was NOT exercised, and that a green verdict survives, is this one: the gate is GREEN,
# then the lease lapses with NO push and NO body edit, and the merge is attempted.
#
# ONE world, ONE PR body, ONE head SHA, evaluated at two instants. Nothing about the pull request
# changes between the two legs — that is the whole point, and it is why `--now` exists rather than a
# `sleep`. A gate that only ever covered expiry-before-`delivery` stays green through this defect.
# =============================================================================================
W9="$WORK/w9"; comment_at "$W9" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
BODY9="$(marker "$REPO#2656" 5300000042 "$HEAD_SHA")"

expect "AC4: at T+60 the verdict is GREEN" \
  0 "OK" "$W9" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" "$BODY9" --now "$(at 60)"

# ...and the green publishes the instant it stops being true, derived from that marker's own lease.
expect "AC4: the green publishes its own deadline (T0 + the marker's 120-minute lease)" \
  0 "verdict-expires-at=$(at 120)" "$W9" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" "$BODY9" \
  --now "$(at 60)"

expect "AC4: at T+121 the SAME unchanged PR is RED — no push, no body edit, only time passing" \
  1 "claim LEASE HAS LAPSED" "$W9" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" "$BODY9" --now "$(at 121)"

expect "AC4: and the red names the holder, so expiry cannot be misread as a lost race" \
  1 "still names \`osprey-174c\`" "$W9" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" "$BODY9" \
  --now "$(at 121)"

# =============================================================================================
# 10. .github#2656 AC1 — THE RE-EVALUATION ITSELF (`--sweep`). Sections 8 and 9 prove the VERDICT is a
# function of time; they cannot prove a merge is fenced, because nothing in GitHub recomputes a check
# run when a lease lapses. `--sweep` is what recomputes it. These legs assert what it publishes, and —
# just as importantly — the two cases where it must publish NOTHING.
# =============================================================================================
sweep() {  # sweep <world> [extra args…]
  local world="$1"; shift
  PATH="$STUB:$PATH" WORLD="$world" FSGG_CLAIM_GEN_TRIES=1 FSGG_CLAIM_GEN_RETRY_DELAY=0 \
    python3 "$TOOL" --sweep --repo "$REPO" "$@" 2>&1
}

# posted <world>  — every check run the sweep published, as one blob (empty when it published none).
posted() { cat "$1/posted.ndjson" 2>/dev/null || true; }

sweep_expect() {  # sweep_expect <name> <want-rc> <needle> <posted-needle|-> <world> [extra…]
  local name="$1" want="$2" needle="$3" want_posted="$4"; shift 4
  local out rc=0
  out="$(sweep "$@")" || rc=$?
  local got_posted; got_posted="$(posted "$1")"
  # WHAT IT PUBLISHED IS ASSERTED BEFORE WHAT IT SAID. The sweep writes a REQUIRED context's verdict,
  # so "it published a check run it had no business publishing" is the failure a reader must see first;
  # a mutation that removes one of the two refusals also removes the log line that names it, and
  # grading the prose first would report the missing sentence and never mention the stray write.
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ "$want_posted" = "-" ] && [ -n "$got_posted" ]; then
    bad "$name (it published a check run and must not have)" "$got_posted"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  elif [ "$want_posted" != "-" ] && ! grep -qF "$want_posted" <<<"$got_posted"; then
    bad "$name (published nothing matching '$want_posted')" "${got_posted:-<nothing published>}"
  else
    ok "$name"
  fi
}

SHA_A="$(printf '%040d' 11)"
REF_A="item/2656-lease-expiry"
BODY_A="$(marker "$REPO#2656" 5300000042 "$SHA_A")"

# (a) THE DEFECT, CLOSED: the standing verdict is `success`, the lease has since lapsed with no push
#     and no body edit — the sweep revokes it.
WSA="$WORK/wsa"
comment_at "$WSA" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSA" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSA" "$SHA_A" success
sweep_expect "sweep: a green whose lease has since lapsed is REVOKED" \
  0 "success -> failure [expired]" '"conclusion": "failure"' "$WSA" --now "$(at 121)"

# (b) THIS LEG WAS INVERTED IN REPAIR 1, AND THE INVERSION IS THE POINT (F2 of `.github#2673`'s round 1).
#     It used to assert that the sweep RESTORES a green once the claim is live again, and it passed —
#     which is exactly why the leak survived authoring: the fixture PINNED the behaviour that had to
#     change, the `.github#2662` shape. What the round-1 critic measured was `failure -> success` with
#     `"conclusion": "success"` published, i.e. a cron flipping a REQUIRED merge gate green with no
#     repository event behind it. That is a strictly larger power than `.github#2656` asked for and it
#     points the wrong way: revocation can only ever block a merge, granting can admit one.
#
#     The behaviour this leg now asserts is the decision, not a conservatism: the sweep publishes ONLY
#     `success` -> `failure`. The usability case the old leg was protecting — a holder who `heartbeat`s
#     after a lapse and would otherwise sit on a revoked red — is answered by an event-driven or
#     human-initiated path instead (a push, a `delivery` call that really PATCHes the body, or re-running
#     the `claim-generation` job), and the gate's own `[expired]` message now spells that out. Note the
#     refusal is NAMED on stdout rather than silently skipped: "considered it and declined" and "never
#     looked at it" are different facts, and a reader of a scheduled run is owed the difference.
WSB="$WORK/wsb"
comment_at "$WSB" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSB" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSB" "$SHA_A" failure
sweep_expect "sweep: a red whose claim is live again is NOT restored — the sweep never publishes a green" \
  0 "which is not a revocation" "-" "$WSB" --now "$(at 60)"

# ...and the refusal is not an accident of the `[ok]` verdict shape: a red that would move to a DIFFERENT
# red is still not a revocation this sweep publishes, because `success` -> `failure` is the only
# transition it has. (Here the standing verdict is `cancelled` — a real GitHub conclusion — and the fresh
# one is `failure`. Publishing would be defensible; it is still refused, because one transition is easier
# to audit than a lattice of "more restrictive than" judgements on a required merge gate.)
WSB2="$WORK/wsb2"
comment_at "$WSB2" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSB2" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSB2" "$SHA_A" cancelled
sweep_expect "sweep: only success -> failure is published, not every more-restrictive move" \
  0 "cancelled -> failure" "-" "$WSB2" --now "$(at 121)"

# (c) NO CHANGE, NO WRITE. The sweep runs on a schedule; a sweep that re-posted an unchanged verdict
#     every period would bury each PR's checks list under identical check runs.
WSC="$WORK/wsc"
comment_at "$WSC" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSC" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSC" "$SHA_A" success
sweep_expect "sweep: an unchanged verdict publishes nothing" \
  0 "is unchanged (success)" "-" "$WSC" --now "$(at 60)"

# (d) IT NEVER ORIGINATES A VERDICT. A head with no standing `claim-generation` check run has not been
#     judged by the required job, and a cron must not be the first thing to judge it.
WSD="$WORK/wsd"
comment_at "$WSD" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSD" 2657 "$REF_A" "$SHA_A" "$BODY_A"
sweep_expect "sweep: a head with no standing check run is left entirely alone" \
  0 "it never originates" "-" "$WSD" --now "$(at 121)"

# (e) A FAILED READ LEAVES THE STANDING VERDICT ALONE (#266, in the only direction available here).
WSE="$WORK/wse"
mkdir -p "$WSE/comments"; : > "$WSE/comments/${REPO//\//__}__2656.unreachable"
pr "$WSE" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSE" "$SHA_A" success
sweep_expect "sweep: an unreadable claim state warns and publishes nothing" \
  0 "could not be re-evaluated" "-" "$WSE" --now "$(at 121)"

# (f) A NON-ITEM BRANCH IS NOT THE SWEEP'S BUSINESS — the same applicability rule the required job
#     uses, because `item_args` is the only place either of them asks the question.
WSF="$WORK/wsf"
pr "$WSF" 2658 "chore/bump-deps" "$SHA_A" "no marker here"
checkrun "$WSF" "$SHA_A" success
sweep_expect "sweep: a non-item branch is not re-evaluated at all" \
  0 "0 item PR(s) re-evaluated" "-" "$WSF" --now "$(at 121)"

# (g) --dry-run reports the change and publishes nothing, so the mechanism can be exercised against a
#     live repository before it is allowed to write.
WSG="$WORK/wsg"
comment_at "$WSG" "$REPO" 2656 5300000042 "$T0" 120 "osprey-174c"
pr "$WSG" 2657 "$REF_A" "$SHA_A" "$BODY_A"
checkrun "$WSG" "$SHA_A" success
sweep_expect "sweep: --dry-run reports the revocation without publishing it" \
  0 "success -> failure [expired]" "-" "$WSG" --now "$(at 121)" --dry-run

# (h) The sweep takes no per-PR arguments — it reads the whole repository itself. Accepting them would
#     let a caller point "the sweep" at one PR's body while claiming to have swept the repo.
WSH="$WORK/wsh"
sweep_expect "sweep: refuses per-PR arguments rather than silently ignoring them" \
  3 "takes no --head-ref" "-" "$WSH" --head-ref "$REF_A"

# =============================================================================================
# 11. REPAIR 1 — F1 of `.github#2673`'s round 1. A `lease=` of ten digits or more made
# `at + timedelta(minutes=lease)` raise `OverflowError`, which is not a `GateError` and so went past
# `sweep_all`'s per-PR handler and ABORTED THE WHOLE SWEEP: every open PR after the offending one kept
# its standing verdict with nothing to re-evaluate it — the exact defect this item exists to close,
# reachable through the fix for it. Ten digits is not exotic: a claim marker's own `renewed=` token is
# a .NET tick count, ~6.4e17.
#
# Two legs, because the repair has two halves and either alone would leave the leak: the malformed
# lease must FALL BACK (the rule `marker_lease` already stated for every other malformation), and one
# pull request must never be able to end the sweep whatever it does.
# =============================================================================================
HUGE_LEASE=9999999999   # ten digits — over the ~4.2e9-minute point where the arithmetic overflowed

W11A="$WORK/w11a"; comment_at "$W11A" "$REPO" 2656 5300000001 "$T0" "$HUGE_LEASE"
expect "F1: an unrepresentable lease= falls back and SAYS it fell back, instead of crashing" \
  1 "too large to compute an expiry instant from" "$W11A" "$REPO" "item/2656-lease-expiry" \
  "$HEAD_SHA" "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 121)"

# ...and the same marker does not wedge the item either: falling back to 120 means it is judged, not
# skipped. At T+60 it is still live under the fallback, so the verdict is a green — proving the
# fallback is a real lease and not a synonym for "refuse".
expect "F1: the fallen-back marker is still JUDGED — live at T+60 under the fallback lease" \
  0 "OK" "$W11A" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" \
  "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 60)"

# THE SWEEP-WIDE HALF: the offending PR is FIRST in the listing, and a second PR whose green must be
# revoked comes after it. Before the repair, the crash on #2657 meant #2658 was never reached. The
# assertion is that #2658's revocation IS published — i.e. one pull request cannot cost the others
# their re-evaluation.
SHA_B="$(printf '%040d' 12)"
W11B="$WORK/w11b"
comment_at "$W11B" "$REPO" 2656 5300000001 "$T0" "$HUGE_LEASE"
comment_at "$W11B" "$REPO" 2660 5300000077 "$T0" 120 "osprey-174c"
pr "$W11B" 2657 "item/2656-lease-expiry" "$SHA_A" "$(marker "$REPO#2656" 5300000001 "$SHA_A")"
pr "$W11B" 2658 "item/2660-second-pr"    "$SHA_B" "$(marker "$REPO#2660" 5300000077 "$SHA_B")"
checkrun "$W11B" "$SHA_A" success
checkrun "$W11B" "$SHA_B" success
sweep_expect "F1: a pathological lease on one PR does not cost the NEXT PR its revocation" \
  0 "PR #2658 (item/2660-second-pr) success -> failure [expired]" '"head_sha": "'"$SHA_B"'"' \
  "$W11B" --now "$(at 121)"

# =============================================================================================
# 12. REPAIR 1 — F4 of `.github#2673`'s round 1. `[expired]` named the wrong holder whenever two
# lapsed markers declared DIFFERENT leases, because "the youngest marker" and "the one that lapsed most
# recently" are different questions once the leases differ, and the old ordering asked the first.
#
# THE CONSTRUCTION HAS TO BE BUILT DELIBERATELY, and my first attempt at it did NOT discriminate — the
# old ordering happened to give the right answer, and the leg passed against both implementations,
# which is a vacuous test wearing a green badge. Evaluated at T0+300:
#
#   comment 5300000001, `stale-holder`,  lease  30, refreshed T0+185 -> age 115m, LAPSED 85m ago
#   comment 5300000200, `recent-holder`, lease 240, refreshed T0+50  -> age 250m, LAPSED 10m ago
#
# `recent-holder` is the correct answer and is the OLDER marker; every incidental signal points the
# other way, exactly as the critic measured. `stale-holder` has the smaller age, holds the LOWEST id,
# and is the generation this PR's `gen=` names — so an ordering by age, by id, or by "the one the PR
# talks about" all name the wrong worker in the one sentence AC3 exists to make correct.
# =============================================================================================
W12="$WORK/w12"
comment_at "$W12" "$REPO" 2656 5300000001 "$(at 185)" 30  "stale-holder"
comment_at "$W12" "$REPO" 2656 5300000200 "$(at 50)"  240 "recent-holder"
expect "F4: [expired] names the worker who lapsed MOST RECENTLY, not the youngest marker" \
  1 "still names \`recent-holder\`" "$W12" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" \
  "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 300)"

# ...and it reports THAT marker's lease and lapse instant, not the other's — the whole sentence moves
# together or it is still misleading.
expect "F4: and it reports that holder's own lease and lapse instant" \
  1 "its 240-minute lease (declared by that marker) ran out at $(at 290)" "$W12" "$REPO" \
  "item/2656-lease-expiry" "$HEAD_SHA" "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --now "$(at 300)"

# =============================================================================================
# 13. REPAIR 1 — the fallback lease must itself be usable, refused ONCE and up front. `read_claim_state`
# falls back to this number whenever a marker's own `lease=` cannot yield an expiry instant, so a
# fallback that cannot either would turn one malformed marker into a no-verdict — the wedge
# `marker_lease` promises never to cause. Both spellings exit 3; the leg asserts the UP-FRONT message,
# because "the operator gave me an unusable lease" and "this item's markers are unusable" are different
# facts and only one of them is the operator's to fix.
# =============================================================================================
W13="$WORK/w13"; comment_at "$W13" "$REPO" 2656 5300000001 "$T0" 120
expect "repair 1: an unusable --lease-minutes is refused up front, naming the flag" \
  3 "give --lease-minutes (or \$FSGG_CLAIM_LEASE_MIN) a value this gate can add to a timestamp" \
  "$W13" "$REPO" "item/2656-lease-expiry" "$HEAD_SHA" \
  "$(marker "$REPO#2656" 5300000001 "$HEAD_SHA")" --lease-minutes 9999999999

echo
echo "claim-generation fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
