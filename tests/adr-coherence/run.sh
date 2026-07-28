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

# Like expect(), but asserts EVERY remaining argument appears in the output. A multi-target field
# that reports only ONE of its findings is the .github#1637 defect half-fixed, and a single-needle
# assertion cannot tell the difference. Usage: expect_all <want> <dir> <name> <needle>...
expect_all() {
  local want="$1" dir="$2" name="$3"; shift 3
  local rc=0 out needle
  out="$(python3 "$SCRIPT" --dir "$dir" 2>&1)" || rc=$?
  if [ "$rc" != "$want" ]; then
    printf '  FAIL  %s\n        expected exit %s, got %s\n        %s\n' "$name" "$want" "$rc" "${out//$'\n'/$'\n'        }"
    FAIL=$((FAIL + 1))
    return
  fi
  for needle in "$@"; do
    if ! printf '%s' "$out" | grep -qi -- "$needle"; then
      printf '  FAIL  %s\n        exit %s was right, but the message never said %q\n        %s\n' \
        "$name" "$rc" "$needle" "${out//$'\n'/$'\n'        }"
      FAIL=$((FAIL + 1))
      return
    fi
  done
  printf '  ok    %s (exit %s, %s needle(s))\n' "$name" "$rc" "$#"
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

# ------------------------------------------- legs 10 & 11: A FIELD NAMING N RECORDS DECLARES N
# .github#1637. The declaration scan used to stop at the first `.`, and every ADR cross-reference
# is a markdown link ending in `.md` — so a field naming three records declared exactly ONE, and
# the other two links were never examined. Measured on ADR-0067, whose three-target `**Amends:**`
# field yielded `['0011']` while two deliberately stripped back-markers reported "all bidirectional".
#
# These two legs are a pair and only the pair is a test. Leg 10 alone would pass on a gate that
# read nothing; leg 11 is the one the old scan FAILED, and it must name BOTH missing ends, because
# a fix that widens from one target to two is still the same defect at N=3.
multi() { # $1=dir  $2..=numbers whose back-marker to KEEP
  local dir="$1"; shift
  mkdir -p "$dir"
  local n status
  for n in 0001 0002 0003; do
    status="Accepted"
    case " $* " in *" $n "*) status="Accepted — amended by [ADR-0004](0004-four.md)";; esac
    record "$n" "$status" "" > "$dir/$n-$n.md"
  done
  # ONE field, THREE targets, each a markdown link whose target ends in `.md`.
  record 0004 "Accepted" \
    "- **Amends:** [ADR-0001](0001-0001.md) §1, [ADR-0002](0002-0002.md) §2, and [ADR-0003](0003-0003.md) §3" \
    > "$dir/0004-four.md"
  cat > "$dir/README.md" <<'EOF'
| ADR | Title | Status |
|---|---|---|
| [0001](0001-0001.md) | One | Accepted |
| [0002](0002-0002.md) | Two | Accepted |
| [0003](0003-0003.md) | Three | Accepted |
| [0004](0004-four.md) | Four | Accepted |
EOF
}

L10="$TMP/multi-target-ok"; multi "$L10" 0001 0002 0003
expect 0 "$L10" "" "a THREE-target Amends field with all three back-markers is clean"

L11="$TMP/multi-target-broken"; multi "$L11" 0001
expect_all 1 "$L11" "a three-target field missing TWO back-markers names BOTH (not just the first)" \
  "0002-0002.md:1 — ONE-SIDED" "0003-0003.md:1 — ONE-SIDED"

# ------------------------------------------- leg 12: WIDENING MUST NOT MANUFACTURE LINKS
# The other half of #1637: reading to the end of the clause instead of the first `.` must not
# start reading every four-digit number as a record. Each non-target below names a record that
# has NO back-marker, so a manufactured link is an immediate FINDING here, not a silent pass —
# and each of the five was checked by deleting its guard and watching this leg go red:
#
#   [#0003](…/issues/3)   an issue number                 (guard: no `#` before it)
#   0002f19a              a digest                        (guard: no word character after it)
#   §0001                 a section reference             (guard: no `§` before it)
#   ; interacts with …    a clause that is NOT a claim    (guard: `;` closes the clause)
#   . ADR-0003 stays …    a NEW sentence about a record   (guard: sentence-ending `.` closes it)
#
# `2026-07-28` is here for realism and is the one negative this leg CANNOT prove: a date is
# refused twice over — by the `-0` that follows it, and because no ADR is numbered 2026 — so
# deleting either guard leaves the leg green. It is pinned by the reference pattern's own
# comment, not by this corpus.
#
# The `;` case is not hypothetical: ADR-0065's real header reads
# `**Amends:** [ADR-0014](…) Decision 5; interacts with [ADR-0019](…) and [ADR-0062](…)`.
L12="$TMP/no-manufactured-links"; mkdir -p "$L12"
record 0001 "Accepted" "" > "$L12/0001-one.md"
record 0002 "Accepted" "" > "$L12/0002-two.md"
record 0003 "Accepted" "" > "$L12/0003-three.md"
record 0004 "Accepted — amended by [ADR-0005](0005-five.md)" "" > "$L12/0004-four.md"
record 0005 "Accepted" "$(cat <<'EOF'
- **Amends:** [ADR-0004](0004-four.md) §1 — landed 2026-07-28 as [#0003](https://github.com/FS-GG/.github/issues/3), digest 0002f19a, §0001 unchanged; interacts with [ADR-0001](0001-one.md) and [ADR-0002](0002-two.md)
- **Supersedes:** [ADR-0004](0004-four.md) §2 in full. ADR-0003, ADR-0002 and ADR-0001 stay in force, unamended.
EOF
)" > "$L12/0005-five.md"
cat > "$L12/README.md" <<'EOF'
| ADR | Title | Status |
|---|---|---|
| [0001](0001-one.md) | One | Accepted |
| [0002](0002-two.md) | Two | Accepted |
| [0003](0003-three.md) | Three | Accepted |
| [0004](0004-four.md) | Four | Accepted |
| [0005](0005-five.md) | Five | Accepted |
EOF
expect 0 "$L12" "" "a date, an issue number, a digest, a §ref and a non-claim clause are NOT targets"

# ------------------------------------------- legs 13-17: EXECUTION-STATE AGREEMENT (.github#1703)
# The defect: ADR-0067 §5 retired `.codex/skills`; #1636 executed it and amended ADR-0065 in the same
# change; ADR-0011 and ADR-0014 — the other two records ADR-0067 amends — were not touched, and neither
# were their index rows. Assertions 1-4 were ALL GREEN over that corpus. The link ADR-0011 <-> ADR-0067
# is two-sided; assertion 2 is satisfied by a link that says nothing about whether the amendment has
# HAPPENED. Meanwhile index row 130 said EXECUTED 2026-07-28 and row 109 said "D1 still governs today".
#
# `amender` builds that corpus in miniature: 0003 numbers its clauses `**§N — …**`, executes §5, and
# amends 0001 and 0002. $1 chooses what 0001's note records. 0002 is always the record that got the
# amendment right, so every leg carries a control that must stay clean.
amender() { # $1=dir  $2=0001's note about ADR-0003
  local dir="$1" note="$2"
  mkdir -p "$dir"
  record 0001 "Accepted" "- **Amended by:** [ADR-0003](0003-three.md) $note" > "$dir/0001-one.md"
  record 0002 "Accepted" "- **Amended by:** [ADR-0003](0003-three.md) §5 — **EXECUTED 2026-07-28.**" \
    > "$dir/0002-two.md"
  cat > "$dir/0003-three.md" <<'EOF'
# ADR-0003: the amender

- **Status:** Accepted
- **Date:** 2026-07-14
- **Affects:** `.github`
- **Amends:** [ADR-0001](0001-one.md) and [ADR-0002](0002-two.md)

## Context
c
## Decision

**§5 — the clause that executed.** body.

> **EXECUTED 2026-07-28.** [ADR-0002](0002-two.md) was amended in the same change.

**§6 — the clause that did not.** body.

## Consequences
e
EOF
  cat > "$dir/README.md" <<'EOF'
| ADR | Title | Status |
|---|---|---|
| [0001](0001-one.md) | One | Accepted |
| [0002](0002-two.md) | Two | Accepted |
| [0003](0003-three.md) | Three | Accepted |

## Supersession map

| Amended | § | By | What changed |
|---|---|---|---|
| [0001](0001-one.md) | D1 | [0003](0003-three.md) §5 | **EXECUTED 2026-07-28.** |
| [0002](0002-two.md) | D1 | [0003](0003-three.md) §5 | **EXECUTED 2026-07-28.** |
EOF
}

# Leg 13 is the pair's control: 0001 records the execution, so the corpus is clean. Without it,
# legs 14-17 would pass on a gate that reported a finding for every amendment in the corpus.
L13="$TMP/exec-recorded"; amender "$L13" '§5 — **EXECUTED 2026-07-28.**'
expect 0 "$L13" "" "an amended record that RECORDS the execution is clean"

# Leg 14 is the .github#1703 defect itself, reduced: the amendment link is two-sided and the note is
# present — assertion 2 is satisfied — and the note is SILENT about whether §5 happened.
L14="$TMP/exec-unrecorded"; amender "$L14" '— direction only; this record stays IN FORCE.'
expect_all 1 "$L14" "an amended record SILENT about an EXECUTED clause is a FINDING" \
  "0001-one.md:1 — EXECUTION STATE UNRECORDED"

# Leg 15: the escape hatch, and it must actually work — a record amended only by §6, which has NOT
# executed, is not falsified by §5's execution and must not be dragged into a finding. This is the
# leg that keeps assertion 5 from degenerating into "every amended record must say EXECUTED".
L15="$TMP/exec-other-clause"; amender "$L15" '§6 — direction only; §6 is not landed.'
expect 0 "$L15" "" "a record amended by a clause that has NOT executed is clean when it cites it"

# Leg 16: the INDEX half. #1636 flipped row 130 and not row 109, so the table disagreed with itself
# about one flip. Here 0001's RECORD is correct and only its row is stale — the reverse of leg 14,
# because a gate that checked only the records would have called that corpus clean.
L16="$TMP/exec-row-stale"; amender "$L16" '§5 — **EXECUTED 2026-07-28.**'
sed -i 's#| \[0001\](0001-one.md) | D1 | \[0003\](0003-three.md) §5 | \*\*EXECUTED 2026-07-28.\*\* |#| [0001](0001-one.md) | D1 | [0003](0003-three.md) | **Direction only; D1 still governs today.** |#' \
  "$L16/README.md"
expect_all 1 "$L16" "an index row SILENT about an EXECUTED clause is a FINDING" \
  "README.md:11 — EXECUTION STATE UNRECORDED"

# Leg 17: THE MARKER IS A DECLARATION, NOT A WORD ENGLISH CAN SAY BY ACCIDENT.
# Case-insensitive matching was the first implementation and it was measured wrong on the real
# corpus: `already executed the reclassification` (ADR-0033's Affects line) and `the reciprocal
# amendment markers when executed` (ADR-0056:83) both matched, and the gate reported six findings
# against records that have executed nothing. Here ADR-0003 executes NOTHING and merely uses the
# word — assertion 5 must find no amender at all, so the silent 0001 stays clean.
L17="$TMP/lowercase-prose"; amender "$L17" '— direction only; this record stays IN FORCE.'
sed -i 's#> \*\*EXECUTED 2026-07-28.\*\* \[ADR-0002\](0002-two.md) was amended in the same change.#> The reclassification was executed elsewhere and is recorded in the wrong place.#' \
  "$L17/0003-three.md"
sed -i 's#\*\*EXECUTED 2026-07-28.\*\*#executed, allegedly#g' "$L17/README.md" "$L17/0002-two.md"
expect 0 "$L17" "" "lowercase 'executed' in prose is NOT an execution marker"

printf '\nadr-coherence fixture: %s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" -eq 0 ] || exit 1
