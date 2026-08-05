#!/usr/bin/env bash
# Fixture for `render_component_count` in scripts/generate-projections — the `component-count` region
# emitted into profile/README.md (.github#1313), gated here since .github#2245.
#
# WHY THIS FILE EXISTS, AND WHY `generate-projections --check` IS NOT ENOUGH. `--check` compares the
# rendered regions against the committed tree, so it can only ever detect a change that MOVES on the
# roster as it stands today. #2245 changed `$total` from `.repos | length` to the count of FABRIC rows
# (`role != "non-participant"`), and those two expressions are EQUAL on every roster that has no
# non-participant row — which is every roster this repo has ever committed. So the change shipped with
# `--check` green either way: reverting it changed nothing observable, and an inversion that survives
# is not a gate (.github#2245 review round 1, finding F1).
#
# THE PROPERTY, STATED AS ARITHMETIC. The rendered sentence is
#
#     **<N> framework components** ship independently … across **<T>** repositories in the org
#     (those <N> plus this `.github` coordination repo).
#
# so it asserts T == N + 1 in its own words. `$total` must therefore count the org's fabric rows and
# nothing else: a rostered repo that participates in no fabric — which since #2245 may be one the org
# does not even own — is not one of "those N plus this `.github`". With `.repos | length` and one
# non-participant row the region renders "seven … across nine … (those seven plus this `.github`)",
# a self-contradicting count on the PUBLIC org profile page. That is what the legs below catch.
#
# HOW IT RUNS THE REAL CODE. The renderer is a shell function inside a script whose top-level work
# needs a built engine and every projection target on disk, so this fixture EXTRACTS the shipped
# `render_component_count()` from scripts/generate-projections and evaluates that text with `$ROSTER`
# pointed at a throwaway roster. It is the shipped bytes, not a restatement of them — a mutation to
# the source is a mutation to what these legs run. The extraction is itself asserted (non-empty, and
# it really contains the jq program), so a rename or a refactor that moves the logic elsewhere REDS
# here rather than passing over an empty function.
#
# No network, no engine, throwaway trees. Mirrors tests/repos-registry/run.sh in shape.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
GEN="$HERE/../../scripts/generate-projections"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/component-count-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "component-count fixture"

# --- extract the shipped renderer, and refuse to run over nothing --------------------------------
FN="$WORK/render.sh"
sed -n '/^render_component_count() {$/,/^}$/p' "$GEN" > "$FN"
if [ ! -s "$FN" ]; then
  echo "::error::component-count: render_component_count() was not found in scripts/generate-projections."
  echo "::error::  The fixture runs the SHIPPED function; with nothing to run every leg below would"
  echo "::error::  pass vacuously. If the renderer was renamed or moved, update this extraction."
  exit 1
fi
if ! grep -q 'jq -r' "$FN"; then
  bad "the extracted renderer still carries its jq program" "$(cat "$FN")"
else
  ok "the shipped render_component_count() was extracted and carries its jq program"
fi
if ! grep -q '^}$' "$FN"; then
  echo "::error::component-count: the extraction did not reach the function's closing brace." >&2
  exit 1
fi

# roster <name> <extra-rows> — the authority + N framework rows, plus whatever else a leg needs.
# Only the fields `render_component_count` reads are present; this is not a `repos.sh validate`
# subject (tests/repos-registry/run.sh owns that) and deliberately does not pretend to be.
roster() {
  local n="$1" extra="$2"
  local f="$WORK/$n.json"
  python3 - "$f" "$extra" <<'PY'
import json, sys
out, extra = sys.argv[1], sys.argv[2]
repos = [{"id": ".github", "full": "FS-GG/.github", "role": "authority", "receives": ["labels"]}]
repos += [{"id": f"fw{i}", "full": f"FS-GG/FS.GG.F{i}", "role": "framework", "receives": ["labels"]}
          for i in range(1, 8)]                      # seven framework components, as today
if extra:
    repos += json.loads(extra)
json.dump({"schemaVersion": 11, "authority": "FS-GG/.github", "repos": repos}, open(out, "w"))
PY
  printf '%s' "$f"
}

# render <roster-json> — run the SHIPPED function over it and print the sentence line.
render() {
  ( set -euo pipefail
    # SC2034 (`ROSTER appears unused`): it is read by the SHIPPED `render_component_count`, which is
    # sourced on the next line from a file assembled at runtime — shellcheck cannot follow a dynamic
    # source, so it cannot see the use. Renaming this variable would break the fixture at runtime,
    # which is the check that matters; the directive records why the static one cannot see it.
    # shellcheck disable=SC2034
    ROSTER="$1"
    # shellcheck disable=SC1090  # the source IS the subject under test; see the header
    . "$FN"
    render_component_count ) | grep -F 'framework components' || true
}

# WORD -> INT for the two numerals the sentence carries, so the legs assert ARITHMETIC rather than a
# pinned string. A pinned string would also have to be edited by whoever changes the wording, and
# would then stop testing the property while still looking like a test.
num() {
  python3 - "$1" <<'PY'
import re, sys
words = {"one":1,"two":2,"three":3,"four":4,"five":5,"six":6,
         "seven":7,"eight":8,"nine":9,"ten":10,"eleven":11,"twelve":12}
line = sys.argv[1]
found = [words[w.lower()] for w in re.findall(r"\*\*([A-Za-z]+)", line) if w.lower() in words]
found += [words[w.lower()] for w in re.findall(r"\*\*([A-Za-z]+)\*\*", line) if w.lower() in words]
print(" ".join(str(x) for x in found[:2]) if len(found) >= 2 else "")
PY
}

# assert_coherent <name> <roster-json> <expected-fw> <expected-total>
assert_coherent() {
  local name="$1" reg="$2" want_fw="$3" want_total="$4" line got fw total
  line="$(render "$reg")"
  if [ -z "$line" ]; then bad "$name (the renderer emitted no sentence)" "$(render "$reg")"; return; fi
  got="$(num "$line")"
  fw="${got%% *}"; total="${got##* }"
  if [ -z "$got" ]; then bad "$name (could not read two numerals out of the sentence)" "$line"; return; fi
  if [ "$fw" != "$want_fw" ]; then bad "$name (framework count $fw, want $want_fw)" "$line"; return; fi
  if [ "$total" != "$want_total" ]; then bad "$name (repository count $total, want $want_total)" "$line"; return; fi
  # THE INVARIANT THE SENTENCE STATES IN WORDS. Asserted independently of the two expectations above
  # so that a leg which updates both numbers together still cannot smuggle an incoherent sentence in.
  if [ "$total" -ne $(( fw + 1 )) ]; then
    bad "$name (INCOHERENT: it says '$fw framework components … across $total repositories … those $fw plus this .github')" "$line"
    return
  fi
  ok "$name"
}

# --- 1. the shape this repo ships today ----------------------------------------------------------
assert_coherent "an all-participant roster renders a coherent count (7 framework + .github = 8)" \
  "$(roster plain '')" 7 8

# --- 2. THE LEG .github#2245 EXISTS FOR, and the one M12 must red --------------------------------
# A rostered NON-PARTICIPANT — since #2245 possibly one the org does not own — is not a fabric row,
# so it must not move either numeral. With `$total` reverted to `.repos | length` this renders
# "**Seven** … across **nine** … (those seven plus this `.github` coordination repo)": the expected
# total fails, AND the coherence check fails, so the leg cannot be satisfied by editing one number.
assert_coherent "a NON-PARTICIPANT row does not inflate the repository count (#2245)" \
  "$(roster nonpart '[{"id":"sir","full":"EHotwagner/S.I.R.","role":"non-participant","receives":[],"reason":"user-owned, no fabric"}]')" \
  7 8

# Two of them, so the leg cannot be passed by an off-by-one that happens to absorb exactly one row.
assert_coherent "TWO non-participant rows still do not inflate the repository count" \
  "$(roster nonpart2 '[{"id":"sir","full":"EHotwagner/S.I.R.","role":"non-participant","receives":[],"reason":"a"},{"id":"other","full":"someone/Else","role":"non-participant","receives":[],"reason":"b"}]')" \
  7 8

# --- 3. the count still FOLLOWS the roster, which is the whole point of the region ----------------
# #1313's defect was a hand-typed count that rotted when a repo was added. Excluding non-participants
# must not turn the number into a constant: adding a real FRAMEWORK row still moves both numerals.
assert_coherent "adding a FRAMEWORK row still moves both numerals (the region is still a projection)" \
  "$(roster grown '[{"id":"fw8","full":"FS-GG/FS.GG.F8","role":"framework","receives":["labels"]}]')" \
  8 9

# ...including alongside a non-participant, which is the mixed roster the org will actually have once
# .github#2206's disposition lands.
assert_coherent "a grown fabric NEXT TO a non-participant row counts the fabric only" \
  "$(roster mixed '[{"id":"fw8","full":"FS-GG/FS.GG.F8","role":"framework","receives":["labels"]},{"id":"sir","full":"EHotwagner/S.I.R.","role":"non-participant","receives":[],"reason":"user-owned, no fabric"}]')" \
  8 9

echo "component-count fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::component-count fixture FAILED"; exit 1; }
echo "component-count fixture — OK"
