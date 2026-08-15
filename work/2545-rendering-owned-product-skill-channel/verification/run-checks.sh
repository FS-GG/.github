#!/usr/bin/env bash
# Re-executes the verification obligations plan.md records for .github#2545, and emits a JUnit report.
#
# Every assertion below RUNS the real gate — `scripts/fsgg-skill-registry-check`'s `delivery-channel`
# arm — against real inputs. None of them greps a source file for a string, and none re-implements
# the arm's rules. VO-006 and VO-008 run it against the SHIPPED `registry/skills.yml` +
# `registry/skills.delivery-channels.yml` pair; the rest build temporary pairs under a scratch
# directory so the tree under test is never mutated.
#
# Hermetic: no network, no board, no producer checkouts. `--repos-root` is an empty scratch dir
# throughout, which is also how VO-005 is measured — the arm is offline by construction, and the
# other arms' noise cannot pass for its verdict because every read is scoped to `[delivery-channel]`.
#
#   usage: work/2545-rendering-owned-product-skill-channel/verification/run-checks.sh [out.xml]
#
# Exit 0 when every check passes; 1 otherwise (and the JUnit report records which).
#
# GATE-INVERSION: `FSGG_2545_INVERT=<vo-name>` flips one check's expected verdict, proving the check
# can go red rather than being structurally incapable of failing. The recorded inversions, their
# exact mutations and their observed output are in verification-evidence.md.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CHECK="$REPO_ROOT/scripts/fsgg-skill-registry-check"
REAL_REG="$REPO_ROOT/registry/skills.yml"
REAL_CH="$REPO_ROOT/registry/skills.delivery-channels.yml"
INVERTER="$REPO_ROOT/tests/skill-registry/invert-rendering-channel.py"
OUT="${1:-$REPO_ROOT/work/2545-rendering-owned-product-skill-channel/verification/junit.xml}"
INVERT="${FSGG_2545_INVERT:-}"

[ -f "$CHECK" ] || { echo "checker missing: $CHECK" >&2; exit 1; }
[ -f "$REAL_REG" ] && [ -f "$REAL_CH" ] || { echo "the shipped registry/declaration pair is missing" >&2; exit 1; }
[ -f "$INVERTER" ] || { echo "inverter missing: $INVERTER" >&2; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-2545-verify.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
EMPTY="$WORK/no-producers"
mkdir -p "$EMPTY"

PASS=0; FAIL=0; CASES=""

record() { # name, ok(0/1), message
  local name="$1" ok="$2" msg="$3"
  if [ "$ok" -eq 0 ]; then
    PASS=$((PASS+1)); CASES="$CASES    <testcase classname=\"github2545\" name=\"$name\"/>
"
  else
    FAIL=$((FAIL+1)); CASES="$CASES    <testcase classname=\"github2545\" name=\"$name\"><failure message=\"$msg\"/></testcase>
"
    echo "FAIL $name: $msg" >&2
  fi
}

# The `[delivery-channel]` lines only. The other arms fire freely against an empty --repos-root, and
# treating their output as this arm's verdict is exactly the kind of measurement that reports a green
# it never established.
dc() { python3 "$CHECK" --registry "$1" --repos-root "$EMPTY" 2>&1 | grep '\[delivery-channel\]' || true; }

# expect_quiet <name> <registry> — the arm reports nothing for this pair.
expect_quiet() {
  local name="$1" reg="$2" want="quiet" got
  [ "$INVERT" = "$name" ] && want="loud"
  got="$(dc "$reg")"
  if [ -n "$got" ]; then
    [ "$want" = "loud" ] && { record "$name" 0 ""; return; }
    record "$name" 1 "expected no delivery-channel finding, got: $(head -1 <<<"$got")"
  else
    [ "$want" = "loud" ] && { record "$name" 1 "inverted: expected a finding and the pair was quiet"; return; }
    record "$name" 0 ""
  fi
}

# expect_finding <name> <registry> <substring...> — the arm reports, and says these things.
expect_finding() {
  local name="$1" reg="$2"; shift 2
  local want="loud" got missing=""
  [ "$INVERT" = "$name" ] && want="quiet"
  got="$(dc "$reg")"
  for needle in "$@"; do
    grep -qF -- "$needle" <<<"$got" || missing="$missing '$needle'"
  done
  if [ "$want" = "quiet" ]; then
    if [ -z "$got" ]; then record "$name" 0 ""; else record "$name" 1 "inverted: expected quiet, got a finding"; fi
    return
  fi
  if [ -z "$got" ]; then
    record "$name" 1 "expected a delivery-channel finding and the pair was quiet"
  elif [ -n "$missing" ]; then
    record "$name" 1 "finding does not say:$missing — got: $got"
  else
    record "$name" 0 ""
  fi
}

# pair <name> <registry-yaml> <channels-yaml> — writes a temp registry/declaration pair, echoes path.
pair() {
  local name="$1"
  printf '%s' "$2" > "$WORK/$name.yml"
  printf '%s' "$3" > "$WORK/$name.delivery-channels.yml"
  printf '%s' "$WORK/$name.yml"
}

BASE_REG='schemaVersion: 1
updated: "2026-08-15"
parameters: [profile]
skills:
  - { id: alpha, scope: process, owner: owner-one, source: One/alpha/SKILL.md, sha256: "0000000000000000000000000000000000000000000000000000000000000000", materializes-when: always }
  - { id: beta,  scope: product, owner: owner-two, source: Two/beta/SKILL.md,  sha256: "0000000000000000000000000000000000000000000000000000000000000000", materializes-when: "profile in [game]" }
'
ONE_ONLY='schemaVersion: 1
classes:
  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: Fixture, evidence: e }
'
BOTH='schemaVersion: 1
classes:
  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: Fixture, evidence: e }
  - { owner: owner-two, scope: product, disposition: delivered, kind: package, channel: Fixture, evidence: e }
'

# --- VO-001: a class the catalog carries and the declaration ignores -----------------------------
expect_finding VO-001 "$(pair vo001 "$BASE_REG" "$ONE_ONLY")" \
  "[delivery-channel] owner-two/product" "beta" "supplied from nowhere"

# --- VO-002: a declaration entry the catalog no longer carries -----------------------------------
expect_finding VO-002 "$(pair vo002 "$BASE_REG" "$BOTH"'  - { owner: owner-ghost, scope: driver, disposition: gap, tracked-by: FS-GG/FS.GG.Rendering#1240 }
')" "[delivery-channel] owner-ghost/driver" "no such row"

# --- VO-003: `tracked-by` must be a reference a reader can resolve -------------------------------
PS_HEAD='schemaVersion: 1
classes:
  - { owner: owner-one, scope: process, disposition: delivered, kind: in-code, channel: Fixture, evidence: e }
  - { owner: owner-two, scope: product, disposition: provider-scoped, kind: template-payload, provider: p, evidence: e, '
expect_finding VO-003 "$(pair vo003 "$BASE_REG" "$PS_HEAD"'tracked-by: .github#1240 }
')" "full owner/repo#number"

# --- VO-004: exactly one accountable answer on a provider-scoped class ---------------------------
expect_finding VO-004 "$(pair vo004 "$BASE_REG" "$PS_HEAD"'provider: p }
')" "EXACTLY ONE"

# --- VO-005: the arm reaches a verdict with NO producer checkout ---------------------------------
# Every check here already runs against an empty --repos-root; this one asserts the property named,
# by requiring a real verdict (a finding with content) out of a run that has no producer trees at all.
if [ -n "$(find "$EMPTY" -mindepth 1 -print -quit)" ]; then
  record VO-005 1 "the scratch --repos-root is not empty, so this measures nothing"
else
  expect_finding VO-005 "$(pair vo005 "$BASE_REG" "$ONE_ONLY")" "[delivery-channel] owner-two/product"
fi

# --- VO-006: GATE-INVERSION on the SHIPPED pair --------------------------------------------------
# Drop the one entry this item is about, and the arm must name the class, the row this item started
# from, and the class's row count.
cp "$REAL_REG" "$WORK/vo006.yml"
if python3 "$INVERTER" "$REAL_CH" "$WORK/vo006.delivery-channels.yml" >/dev/null 2>&1; then
  expect_finding VO-006 "$WORK/vo006.yml" \
    "[delivery-channel] fs-gg-rendering/product" "fs-gg-feedback-report" "18 row(s)"
else
  record VO-006 1 "the inverter could not remove a fs-gg-rendering/product entry from $REAL_CH"
fi

# --- VO-007: the workflow that runs this gate SELECTS the declaration ----------------------------
# Read from the workflow file through a YAML parser, not by grepping the repo for the filename: a
# filter entry under the wrong trigger is the .github#1606 shape, and a grep cannot see the
# difference.
VO7="$(python3 - "$REPO_ROOT/.github/workflows/skill-registry-coherence.yml" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1]))
# PyYAML resolves the bare key `on` to the boolean True (YAML 1.1); accept either spelling.
triggers = doc.get("on", doc.get(True))
want = "registry/skills.delivery-channels.yml"
missing = [t for t in ("pull_request", "push")
           if want not in (triggers.get(t) or {}).get("paths", [])]
print("MISSING:" + ",".join(missing) if missing else "OK")
PY
)"
if [ "$INVERT" = "VO-007" ]; then
  [ "$VO7" = "OK" ] && record VO-007 1 "inverted: expected the filter to be missing the path" || record VO-007 0 ""
elif [ "$VO7" = "OK" ]; then
  record VO-007 0 ""
else
  record VO-007 1 "skill-registry-coherence.yml does not select the declaration: $VO7"
fi

# --- VO-008: the gate this change adds passes on the tree this change lands ----------------------
expect_quiet VO-008 "$REAL_REG"

# --- FR-006: the fixture suite that measures this arm actually runs, and covers its cases --------
# Executed, not asserted about. `tests/skill-registry/run.sh` is hermetic (temp registries + temp
# producer trees, no network), so this package can run the real suite rather than claim it passes.
FR6_LOG="$WORK/skill-registry-suite.log"
if bash "$REPO_ROOT/tests/skill-registry/run.sh" > "$FR6_LOG" 2>&1; then FR6_RC=0; else FR6_RC=1; fi
FR6_MISSING=""
for c in 69 70 71 72 73 74 75 76 77; do
  grep -q "^== $c\. " "$FR6_LOG" || FR6_MISSING="$FR6_MISSING $c"
done
[ "$INVERT" = "FR-006" ] && FR6_RC=$((1 - FR6_RC))
if [ "$FR6_RC" -ne 0 ]; then
  record FR-006 1 "tests/skill-registry/run.sh did not pass: $(tail -3 "$FR6_LOG" | tr '\n' ' ')"
elif [ -n "$FR6_MISSING" ]; then
  record FR-006 1 "the suite ran but never reached delivery-channel case(s):$FR6_MISSING"
else
  record FR-006 0 ""
fi

# --- FR-008: every Rendering-owned product row is dispositioned by name in spec.md ---------------
# A completeness check between two artefacts, not a grep for a section's own name: add a 19th
# fs-gg-rendering product row to the catalog and this reds until the record answers for it too.
FR8="$(python3 - "$REAL_REG" "$REPO_ROOT/work/2545-rendering-owned-product-skill-channel/spec.md" <<'PY'
import sys, yaml
rows = yaml.safe_load(open(sys.argv[1]))["skills"]
ids = [r["id"] for r in rows
       if r.get("owner") == "fs-gg-rendering" and r.get("scope") == "product"]
record = open(sys.argv[2]).read()
# Anchored at line start: AC-008's own prose NAMES this heading, and an unanchored find() lands
# there instead — a check that would have measured the wrong section and reported it as empty.
start = record.find("\n## Disposition of the Rendering-owned product rows\n")
end = record.find("\n## ", start + 1)
section = record[start:end] if start != -1 else ""
missing = [i for i in ids if f"`{i}`" not in section]
print("MISSING:" + ",".join(missing) if (missing or not section) else f"OK:{len(ids)}")
PY
)"
[ "$INVERT" = "FR-008" ] && { case "$FR8" in OK:*) FR8="INVERTED";; *) FR8="OK:inverted";; esac; }
case "$FR8" in
  OK:18) record FR-008 0 "" ;;
  OK:*)  record FR-008 1 "the disposition table covers every row, but the class size moved: $FR8" ;;
  *)     record FR-008 1 "spec.md's disposition section does not name: $FR8" ;;
esac

# --- PC-001 / DEC-002: registry/skills.yml is BYTE-UNCHANGED by this item ------------------------
# The contract claim, measured rather than asserted. registry/skills.yml is the surface of the
# `registry-schema` contract (owner sdd, consumers [github]); the whole reason the declaration is a
# separate file is that this item has no mandate to move that surface.
if git -C "$REPO_ROOT" diff --quiet origin/main -- registry/skills.yml; then PC1=0; else PC1=1; fi
[ "$INVERT" = "PC-001" ] && PC1=$((1 - PC1))
if [ "$PC1" -eq 0 ]; then
  record PC-001 0 ""
else
  record PC-001 1 "registry/skills.yml differs from origin/main — this item declares it untouched"
fi

# --- PM-001: an unknown declaration schema is REFUSED, never parsed optimistically ---------------
# The migration posture plan.md records. A future shape read as today's shape would be a green
# verdict about fields that no longer mean what this arm thinks they mean — a fail-open one level up
# from the classes themselves, which is the whole reason this arm exists.
expect_finding PM-001 "$(pair pm001 "$BASE_REG" 'schemaVersion: 99
classes: []
')" "refusing to read a shape it does not know"

mkdir -p "$(dirname "$OUT")"
{
  printf '<?xml version="1.0" encoding="UTF-8"?>\n'
  printf '<testsuites>\n  <testsuite name="github2545-delivery-channel" tests="%d" failures="%d">\n' \
    "$((PASS + FAIL))" "$FAIL"
  printf '%s' "$CASES"
  printf '  </testsuite>\n</testsuites>\n'
} > "$OUT"

echo "2545 verification: $PASS passed, $FAIL failed -> $OUT"
[ "$FAIL" -eq 0 ]
