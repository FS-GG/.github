#!/usr/bin/env bash
# Fixture for scripts/check-adr-coherence.py — prove the gate can say NO before anyone believes
# it when it says yes (.github#266).
#
# Every leg below is a defect the corpus ACTUALLY HAD on 2026-07-14, reduced to its smallest
# reproduction. Leg 2 is commit c08ebce. Leg 3 is the ADR-0015/ADR-0037 link that sat one-sided
# for twelve days while a governed registry comment asserted it had been fixed. Legs 4 and 5 are
# the fail-open guard: a gate that reads NOTHING and reports green would certify the whole corpus
# without opening a file, which is the #266 shape inside the gate written to close it.
#
# No network, no dependencies. Builds a synthetic corpus in a temp dir and points --dir at it.
set -euo pipefail

SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/scripts/check-adr-coherence.py"
PASS=0
FAIL=0

# Assert the gate exits with $1 on the corpus in $2, and (optionally) that its output mentions $3.
expect() {
  local want="$1" dir="$2" needle="${3:-}" name="$4"
  local rc=0 out
  out="$(python3 "$SCRIPT" --dir "$dir" 2>&1)" || rc=$?
  if [ "$rc" != "$want" ]; then
    printf '  FAIL  %s\n        expected exit %s, got %s\n        %s\n' "$name" "$want" "$rc" "${out//$'\n'/$'\n'        }"
    FAIL=$((FAIL + 1))
    return
  fi
  if [ -n "$needle" ] && ! printf '%s' "$out" | grep -qi -- "$needle"; then
    printf '  FAIL  %s\n        exit %s was right, but the message never said %q\n        %s\n' \
      "$name" "$rc" "$needle" "${out//$'\n'/$'\n'        }"
    FAIL=$((FAIL + 1))
    return
  fi
  printf '  ok    %s (exit %s)\n' "$name" "$rc"
  PASS=$((PASS + 1))
}

# A well-formed record. $1=num $2=status $3=extra body lines
record() {
  cat <<EOF
# ADR-$1: a record

- **Status:** $2
- **Date:** 2026-07-14
- **Affects:** \`.github\`
$3

## Context
c
## Decision
d
## Consequences
e
EOF
}

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ---------------------------------------------------------------- leg 1: a coherent corpus
L1="$TMP/coherent"; mkdir -p "$L1"
record 0001 "Accepted" "" > "$L1/0001-one.md"
record 0002 "Accepted — amended by [ADR-0003](0003-three.md)" "" > "$L1/0002-two.md"
record 0003 "Accepted" "- **Amends:** [ADR-0002](0002-two.md) §1" > "$L1/0003-three.md"
cat > "$L1/README.md" <<'EOF'
| ADR | Title | Status |
|---|---|---|
| [0001](0001-one.md) | One | Accepted |
| [0002](0002-two.md) | Two | Accepted |
| [0003](0003-three.md) | Three | Accepted |
EOF
expect 0 "$L1" "" "a coherent corpus passes"

# ---------------------------------------------------------------- leg 2: THE c08ebce DEFECT
# The record was advanced to Accepted; the index table was never touched. This is the one that
# told every reader, for weeks, that the FS.GG.Game extraction was an unratified proposal.
L2="$TMP/status-drift"; cp -r "$L1" "$L2"
sed -i 's#| \[0001\](0001-one.md) | One | Accepted |#| [0001](0001-one.md) | One | Proposed |#' "$L2/README.md"
expect 1 "$L2" "DISAGREE" "a status the index and the record disagree about is a FINDING"

# ---------------------------------------------------------------- leg 3: THE ONE-SIDED LINK
# 0003 says it amends 0002; 0002 has never heard of 0003. The reader who opens 0002 — the one
# doing the right thing — is the one who gets misled.
L3="$TMP/one-sided"; cp -r "$L1" "$L3"
record 0002 "Accepted" "" > "$L3/0002-two.md"   # strip 0002's forward pointer
expect 1 "$L3" "ONE-SIDED" "an amendment with only one end is a FINDING"

# ---------------------------------------------------------------- leg 4: FAIL-OPEN GUARD (empty)
# An empty corpus must be NO VERDICT (3), never OK (0). "I found no ADRs" is a broken path, not
# a clean corpus — and a gate that reported green here would pass the whole corpus without
# opening a single file.
L4="$TMP/empty"; mkdir -p "$L4"
printf '| ADR | Title | Status |\n|---|---|---|\n' > "$L4/README.md"
expect 3 "$L4" "NO VERDICT" "an EMPTY corpus is a no-verdict, NOT a pass"

# ---------------------------------------------------------------- leg 5: FAIL-OPEN GUARD (index)
# The table's shape moved and the gate can no longer parse a single row. That must fail LOUD.
# If it read zero rows and compared zero statuses, every record would trivially "agree".
L5="$TMP/unparseable"; cp -r "$L1" "$L5"
printf 'The ADR index is now a prose list, not a table.\n\n- 0001 One (Accepted)\n' > "$L5/README.md"
expect 3 "$L5" "NO VERDICT" "an index the gate cannot parse is a no-verdict, NOT a pass"

# ---------------------------------------------------------------- leg 6: an orphan record
L6="$TMP/orphan"; cp -r "$L1" "$L6"
record 0004 "Accepted" "" > "$L6/0004-four.md"   # file exists, no row
expect 1 "$L6" "NO ROW" "a record with no index row is a FINDING"

# ---------------------------------------------------------------- leg 7: a ghost row
L7="$TMP/ghost"; cp -r "$L1" "$L7"
printf '| [0009](0009-gone.md) | Gone | Accepted |\n' >> "$L7/README.md"
expect 1 "$L7" "does not exist" "an index row pointing at no file is a FINDING"

# ---------------------------------------------------------------- leg 8: a tombstone is LEGAL
# Withdrawn numbers are retired, not reused (docs/adr/README.md). A `~~NNNN~~` row with no file
# is the CORRECT shape and must not be reported as a ghost.
L8="$TMP/tombstone"; cp -r "$L1" "$L8"
printf '| ~~0010~~ | *declined* | **Withdrawn** |\n' >> "$L8/README.md"
expect 0 "$L8" "" "a ~~NNNN~~ tombstone row with no file is LEGAL"

# ---------------------------------------------------------------- leg 9: shape
L9="$TMP/shape"; cp -r "$L1" "$L9"
sed -i '/\*\*Affects:\*\*/d' "$L9/0001-one.md"
expect 1 "$L9" "Affects" "a record missing a header field is a FINDING"

printf '\nadr-coherence fixture: %s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ] || exit 1
