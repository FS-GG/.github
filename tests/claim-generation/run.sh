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
#
# An ABSENT comments file is a 404 — this item does not exist. That is deliberately different from a
# PRESENT, empty (or marker-free) file, which is a real "this item exists and nobody holds it" answer
# (the engine's own `released` sentinel). Collapsing the two would make "wrong item number" and
# "correct item, currently unclaimed" indistinguishable, which is exactly the pair `check-claim-
# generation.py`'s `Missing` vs. `RELEASED` handling is written to keep apart.
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
path=""
for a in "$@"; do case "$a" in repos/*) path="$a";; esac; done

notfound()  { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
forbidden() { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
apifail()   { echo "gh: API rate limit exceeded for installation (HTTP 500)" >&2; exit 1; }

rest="${path#repos/}"
case "$rest" in
  */issues/*/comments)
    repo="${rest%%/issues/*}"; num="${rest#*/issues/}"; num="${num%/comments}"
    slug="${repo//\//__}__${num}"
    [ -e "$WORLD/comments/$slug.forbidden" ]   && forbidden
    [ -e "$WORLD/comments/$slug.unreachable" ] && apifail
    f="$WORLD/comments/$slug.ndjson"
    [ -f "$f" ] || notfound
    cat "$f"
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
  1 "not currently held by anyone" "$W2R" "$REPO" "$REF" "$HEAD_SHA" \
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
expect "lease: the SAME marker is STALE under a tightened 30-minute lease" \
  1 "not currently held by anyone" "$W6" "$REPO" "$REF" "$HEAD_SHA" \
  "$(marker "$ITEM" 5000000001 "$HEAD_SHA")" --lease-minutes 30

# =============================================================================================
# 7. .github#2488 — THE FIVE-FOR-FIVE SCENARIO, NAMED. A freshly opened item/<n>-* PR, exactly as
# GitHub's `opened` event would present it BEFORE the .github#2488 self-heal step (`coherence.yml`'s
# `claim-generation` job) has had a chance to run: a real live claim exists, but the PR body carries no
# `fsgg:pr-authorization` marker at all — reds. The SAME item, SAME live claim, and the marker
# `Client.ensureAuthorization`/`Client.authorizationMarker` (`src/FS.GG.Coord.Cli/Client.fs`) would
# write once that self-heal step's live, non---apply `delivery <ref> --pr N` status read reaches it —
# greens. These two legs are cases 1 and 5 above, replayed against THIS item's own ref rather than
# `#2342`'s, so a reader auditing #2488's own acceptance criterion 4 finds the exact scenario by number
# instead of having to infer that the pre-existing legs already cover it.
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

echo
echo "claim-generation fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
