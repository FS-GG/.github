#!/usr/bin/env bash
# Cross-implementation conformance: scripts/skill-union-assert.sh vs Fsgg.SkillMirror.verify —
# FS-GG/.github#1513. The #398 pattern, applied to the org's OTHER unpinned two-implementation pair.
#
# WHAT WAS UNPINNED. .github#120 settled that `Fsgg.SkillMirror` (FS.GG.Contracts) is ADR-0014's "one
# implementation" and that this shell assertion FOLLOWS it. Nothing enforced that sentence, and the two
# diverged three times — each found by a person reading the code, and each only AFTER it had misdirected
# real work:
#   1. a [partitioned] id short-circuited past the byte comparison, so 46 uncompared ids were reported
#      as `byte-identical=4` and two downstream issue bodies were sized against that reading (#1506);
#   2. every count in that summary was printed without its population (#1506);
#   3. checks 1-2 short-circuited past check 3, so an id that was BOTH partitioned AND digest-mismatched
#      reported only [partitioned] and its declared digest was never read (#1513) — and `#1506`'s own
#      fix left `manifest-matched=1` bare, the identical defect one check over.
# Three of one kind is not three accidents. `verify` returns THREE INDEPENDENT FACTS on one `SkillDrift`
# record — `MissingRoots`, `Divergent`, `HashMismatchRoots` — and a follower that computes them in a
# chain can only ever report a PREFIX of them.
#
# WHAT THIS HARNESS DOES. It drives the SHARED VECTOR TABLE (skillmirror.fixtures.json) through the
# shell: each vector is materialized as a real product tree + manifest, the gate is run, and its three
# facts are read back OUT OF ITS DIAGNOSTICS — which is the surface a consumer actually reads, so a
# fact computed internally and never printed does not count as reported. Each fact is then compared
# INDEPENDENTLY against the table, so a shell that gets two right and drops the third fails on the third
# rather than passing on the two.
#
# WHERE THE EXPECTATIONS CAME FROM. The table's `library` column is MEASURED, by `skillmirror-oracle.sh`
# running the library's own source over these exact vectors — not transcribed from the `.fsi` comments,
# which is the failure mode this is about. This harness itself is hermetic: no dotnet, no NuGet, no
# cross-repo checkout, so it runs on every PR and can never be skipped into a green.
#
# A vector carrying a `divergence` block is an INTENTIONAL difference between the two implementations,
# and it is asserted in BOTH directions: the shell must match `shell`, AND `shell` must still differ
# from `library`. A documented difference that quietly disappears is as much a drift as one that grows,
# and neither may be discovered by reading a comment.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# Swappable subject, like run.sh: the `skill-union-bundle` gate points this at dist/skill-union-assert.sh
# from a directory with no lib/ siblings, so the artifact consumers fetch is held to this contract too.
ASSERT="${SKILL_UNION_ASSERT:-$HERE/../../scripts/skill-union-assert.sh}"
FIXTURES="$HERE/skillmirror.fixtures.json"

[ -f "$ASSERT" ]              || { echo "skillmirror-conformance: no assertion script at $ASSERT" >&2; exit 2; }
[ -f "$FIXTURES" ]            || { echo "skillmirror-conformance: fixtures not found: $FIXTURES" >&2; exit 2; }
command -v jq >/dev/null 2>&1 || { echo "skillmirror-conformance: jq required" >&2; exit 2; }

# Guard this harness's OWN fail-open — the very shape it exists to prevent. A corrupt, empty or
# truncated table must never read as green: `jq length` validates the JSON parses (a parse error aborts
# under `set -e` rather than yielding zero rows), the table must be non-empty, and after the loop every
# declared vector must have actually run.
total="$(jq '.fixtures | length' "$FIXTURES")"
[ "$total" -ge 1 ] || { echo "skillmirror-conformance: vector table is empty or malformed: $FIXTURES" >&2; exit 2; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skillmirror-conformance.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; failcount=$((failcount+1)); }

# Space-normalize a root list so `" .a .b"` and `".a .b"` compare equal.
norm() { printf '%s' "$*" | tr -s ' ' ' ' | sed 's/^ //; s/ $//'; }

n=0
while IFS= read -r fx; do
  n=$((n+1))
  name="$(jq -r '.name'   <<<"$fx")"
  id="$(jq -r '.id'       <<<"$fx")"
  scope="$(jq -r '.scope' <<<"$fx")"
  sha="$(jq -r '.sha256'  <<<"$fx")"
  roots="$(jq -r '.roots | join(" ")' <<<"$fx")"
  has_div="$(jq -r 'has("divergence")' <<<"$fx")"

  tree="$WORK/v$n"
  mkdir -p "$tree"
  # An ANCHOR skill, coherent in every root and declared with its true digest. Two jobs: the union is
  # never empty (which is a misconfiguration exit 2, not a verdict), and every configured root
  # directory exists even for a vector whose subject is absent from it (an absent ROOT is a hard exit 2
  # by design — a different fact from an absent skill, and this harness must exercise the second).
  for r in $roots; do
    mkdir -p "$tree/$r/anchor"
    printf '# anchor\n' > "$tree/$r/anchor/SKILL.md"
  done
  # The vector's own copies, exactly as the table states them. Written with `jq -j` — raw output, no
  # added trailing newline — so the JSON string's bytes reach the disk VERBATIM. A command substitution
  # would strip the trailing LF every body ends with and silently change every digest in the table, and
  # `jq -r` would append one of its own; the CRLF vector additionally depends on the CR surviving.
  while IFS= read -r r; do
    [ -n "$r" ] || continue
    mkdir -p "$tree/$r/$id"
    jq -j --arg r "$r" '.bodies[$r]' <<<"$fx" > "$tree/$r/$id/SKILL.md"
  done < <(jq -r '.bodies | keys_unsorted[]' <<<"$fx")

  anchor_sha="$(bash "$ASSERT" --digest "$tree/$(printf '%s' "$roots" | cut -d' ' -f1)/anchor")"
  manifest="$WORK/v$n-manifest.json"
  jq -n --arg a "$anchor_sha" --arg id "$id" --arg scope "$scope" --arg sha "$sha" \
    '{skills: [{id: "anchor", scope: "process", sha256: $a}, {id: $id, scope: $scope, sha256: $sha}]}' \
    > "$manifest"

  out="$WORK/v$n.out"
  rc=0
  bash "$ASSERT" --product "$tree" --roots "$roots" --manifest "$manifest" >"$out" 2>&1 || rc=$?
  if [ "$rc" -eq 2 ]; then
    bad "[$n] $name — the gate exited 2 (misconfiguration), so no verdict was exercised"
    sed 's/^/    | /' "$out"
    continue
  fi

  # Read the three facts back out of the DIAGNOSTICS. A fact the gate computed and did not print is a
  # fact its consumer does not have.
  got_missing="$(norm "$(sed -n "s/^::error::\[partitioned\] skill '$id' is absent from root(s)://p" "$out")")"
  if grep -q "^::error::\[divergent\] skill '$id' " "$out"; then got_divergent=true; else got_divergent=false; fi
  # `[drifted] … in root(s): <root>=<got> …` — strip the observed digests, keep the roots, in order.
  got_mismatch="$(norm "$(sed -n "s/^::error::\[drifted\] skill '$id' SKILL.md digest != manifest [^ ]* in root(s)://p" "$out" \
    | tr ' ' '\n' | sed 's/=.*$//' | tr '\n' ' ')")"

  # A PARSER THAT SILENTLY READS NOTHING IS THIS HARNESS'S OWN FAIL-OPEN, and it fired during
  # development: the digest pattern was written `[0-9a-f]*`, a manifest row was mis-read so the gate
  # printed a NON-HEX expected digest, the `sed` matched nothing, and the vector passed with an empty
  # `hashMismatchRoots` that happened to equal the table. Green over a real defect, which is the exact
  # shape of the three divergences this table exists to catch. So every `[drifted]` line the gate emits
  # for this id must be one this parser RECOGNISED — an unconsumed diagnostic fails loudly instead of
  # being read as an absence of one (#266).
  drifted_lines="$(grep -c "^::error::\[drifted\] skill '$id' " "$out" || true)"
  consumed="$(grep -cE "^::error::\[drifted\] skill '$id' (SKILL\.md digest != manifest [^ ]* in root\(s\):|has no SKILL\.md to digest .* in root\(s\):)" "$out" || true)"
  if [ "$drifted_lines" -ne "$consumed" ]; then
    bad "[$n] $name — the gate printed $drifted_lines [drifted] line(s) for '$id' but this parser recognised $consumed; it is not reading what the gate prints"
    sed 's/^/    | /' "$out"
    continue
  fi

  want_missing="$(norm "$(jq -r '.shell.missingRoots | join(" ")'      <<<"$fx")")"
  want_divergent="$(jq -r '.shell.divergent'                            <<<"$fx")"
  want_mismatch="$(norm "$(jq -r '.shell.hashMismatchRoots | join(" ")' <<<"$fx")")"

  lib_missing="$(norm "$(jq -r '.library.missingRoots | join(" ")'      <<<"$fx")")"
  lib_divergent="$(jq -r '.library.divergent'                           <<<"$fx")"
  lib_mismatch="$(norm "$(jq -r '.library.hashMismatchRoots | join(" ")' <<<"$fx")")"

  # (a) EACH FACT SEPARATELY. Asserting the triple as one blob would let a shell that reports two of
  # three fail with a single message that does not say which — and "which" is the entire subject here.
  fx_ok=1
  [ "$got_missing" = "$want_missing" ] \
    || { bad "[$n] $name — missingRoots: shell '$got_missing' != table '$want_missing'"; fx_ok=0; }
  [ "$got_divergent" = "$want_divergent" ] \
    || { bad "[$n] $name — divergent: shell '$got_divergent' != table '$want_divergent'"; fx_ok=0; }
  [ "$got_mismatch" = "$want_mismatch" ] \
    || { bad "[$n] $name — hashMismatchRoots: shell '$got_mismatch' != table '$want_mismatch'"; fx_ok=0; }
  [ "$fx_ok" -eq 1 ] || { sed 's/^/    | /' "$out"; continue; }

  # (b) …and the table's own coherence with the library, in the direction the vector declares.
  same=0
  [ "$want_missing" = "$lib_missing" ] && [ "$want_divergent" = "$lib_divergent" ] \
    && [ "$want_mismatch" = "$lib_mismatch" ] && same=1
  if [ "$has_div" = "true" ]; then
    if [ "$same" -eq 1 ]; then
      bad "[$n] $name — declares a KNOWN DIVERGENCE but shell and library now agree; delete the divergence block or fix the vector"
    else
      ok "[$n] $name — known divergence held: shell [$want_missing|$want_divergent|$want_mismatch] vs library [$lib_missing|$lib_divergent|$lib_mismatch]"
    fi
  else
    if [ "$same" -eq 1 ]; then
      ok "[$n] $name — all three facts match the library: [$got_missing|$got_divergent|$got_mismatch]"
    else
      bad "[$n] $name — the table's shell block does not equal its library block, and no divergence is declared"
    fi
  fi
done < <(jq -c '.fixtures[]' "$FIXTURES")

[ "$n" -eq "$total" ] || { echo "skillmirror-conformance: ran $n of $total vectors — the table did not stream fully" >&2; exit 2; }

# NON-VACUITY OF THE TABLE ITSELF (#266, #436). A gate that cannot fail is not a gate. The whole point
# is the CONJUNCTION of facts no short-circuiting implementation can express, so the table must actually
# contain a vector with two facts true at once and one with all three — otherwise every vector could be
# satisfied by a chain and this harness would be green over the defect it was written for.
two="$(jq '[.fixtures[] | select((.library.missingRoots | length) > 0 and (.library.hashMismatchRoots | length) > 0)] | length' "$FIXTURES")"
three="$(jq '[.fixtures[] | select((.library.missingRoots | length) > 0 and .library.divergent and (.library.hashMismatchRoots | length) > 0)] | length' "$FIXTURES")"
if [ "$two" -lt 1 ] || [ "$three" -lt 1 ]; then
  echo "::error::skillmirror-conformance: the table has $two partitioned+mismatched and $three all-three vectors — a chain of checks would satisfy every remaining vector, so this harness would be vacuous" >&2
  failcount=$((failcount+1))
fi

echo "--------------------------------------------"
echo "SkillMirror conformance: $pass passed, $failcount failed across $n vector(s)"
[ "$failcount" -eq 0 ] || exit 1
