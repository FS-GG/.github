#!/usr/bin/env bash
# Fixture for scripts/check-emitted-contract-version.py — the gate that compares the registry's
# `governance-handoff` version against SDD's emitted `governanceHandoffContractVersion` constant in
# FS.GG.Contracts/Schemas.fs (.github#1085, epic #266).
#
# The gate exists because a check that passes when its subject is missing manufactures confidence.
# So this fixture spends most of its length on the FAILURE legs: it proves the gate goes red when
# the registry disagrees with the constant, when either file is unreadable, when the constant is
# missing or duplicated, when the `governance-handoff` row is absent, and when YAML has coerced an
# unquoted `version` to a float.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than
# from the thing under test. `must_fail` therefore takes a required pattern.
#
# Throwaway trees under a temp dir. NO NETWORK: the gate reads a checked-out source tree from a
# path, which is why the workflow does the checkout and the gate does not. Mirrors
# tests/source-coherence/run.sh, whose sibling gate this stands beside in source-coherence.yml.

set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-emitted-contract-version.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/emitted-contract-version-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# must_pass <name> <registry> <src-dir>
must_pass() {
  local name="$1" reg="$2" src="$3" out rc
  out="$(python3 "$GATE" "$reg" --contracts-src "$src" 2>&1)" && rc=0 || rc=$?
  if [ "$rc" -ne 0 ]; then bad "$name (expected green, exit $rc)" "$out"; else ok "$name"; fi
}

# must_fail <name> <pattern> <registry> <src-dir> — a non-zero exit is NOT enough. The REASON must
# match, or a gate that dies in argparse would "prove" every claim in this file.
must_fail() {
  local name="$1" pat="$2" reg="$3" src="$4" out rc
  out="$(python3 "$GATE" "$reg" --contracts-src "$src" 2>&1)" && rc=0 || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$name (expected RED, got green)" "$out"
  elif ! grep -qiE "$pat" <<<"$out"; then
    bad "$name (red, but for the wrong reason — wanted /$pat/)" "$out"
  else
    ok "$name"
  fi
}

# ---- source-tree and registry builders --------------------------------------------------------

mksrc() {  # mksrc <dir> <governanceHandoffContractVersion-value>
  local d="$WORK/$1"; mkdir -p "$d"
  # A trimmed Schemas.fs carrying the real shape: several string-valued lets, the target indented
  # inside a module. The gate must pick the NAMED constant out of the noise, not the first string.
  cat > "$d/Schemas.fs" <<FS
namespace Fsgg

module Schemas =
    let agentsVersion = 1
    let someOtherContractVersion = "9.9.9"
    let governanceHandoffContractVersion = "$2"
    let skillManifestVersion = 1
FS
  echo "$d"
}

mkreg() {  # mkreg <name> <yaml-version-literal>
  local f="$WORK/$1.yml"
  cat > "$f" <<YAML
schemaVersion: 1
updated: "2026-07-17"
contracts:
  - { id: governance-handoff, version: $2, range: "1.x", owner: sdd }
  - { id: shared-build-config, version: "1.0.0" }
YAML
  echo "$f"
}

# ---- the GREEN baseline: registry agrees with the emitted constant ----------------------------

GOOD_SRC="$(mksrc good 1.1.0)"
GOOD_REG="$(mkreg good '"1.1.0"')"

must_pass "green: registry version == emitted constant" "$GOOD_REG" "$GOOD_SRC"

# The named constant is picked out of a file full of OTHER string lets — including a decoy
# `someOtherContractVersion = "9.9.9"` that a loose `let ... = "..."` match would read first.
must_pass "green: the NAMED constant is read, not the first string let" "$GOOD_REG" "$GOOD_SRC"

# ---- the assertion itself ---------------------------------------------------------------------

# The real .github#1085 scenario: SDD bumped the emitted constant, the registry has not been flipped.
# This MUST be red — and red HERE, in the repo that owns the registry.
BUMPED_SRC="$(mksrc bumped 1.2.0)"
must_fail "red: emitted constant ahead of registry (the flip is owed)" \
  "is '1\.1\.0' but|1\.2\.0" "$GOOD_REG" "$BUMPED_SRC"

# The other direction: registry advertises a version SDD's constant does not stamp.
AHEAD_REG="$(mkreg ahead '"9.9.9"')"
must_fail "red: registry ahead of emitted constant" "9\.9\.9" "$AHEAD_REG" "$GOOD_SRC"

# ---- fails-open conditions: every one of these must be an ERROR, never a skip -----------------

# An unreadable subject is a failed read, not a coherent tree.
must_fail "red: source tree absent" "cannot read|FAILED READ" "$GOOD_REG" "$WORK/nonexistent-dir"

must_fail "red: registry absent" "cannot read|FAILED READ" "$WORK/nonexistent.yml" "$GOOD_SRC"

# Schemas.fs present but carrying no `governanceHandoffContractVersion` (renamed / moved away).
NOCONST_SRC="$(mksrc noconst 1.1.0)"
cat > "$NOCONST_SRC/Schemas.fs" <<'FS'
namespace Fsgg
module Schemas =
    let agentsVersion = 1
FS
must_fail "red: constant missing from Schemas.fs" "could not find" "$GOOD_REG" "$NOCONST_SRC"

# Two DIFFERENT bindings of the constant: the mirror the #427 fix deleted, trying to reappear.
DUP_SRC="$(mksrc dup 1.1.0)"
cat > "$DUP_SRC/Schemas.fs" <<'FS'
namespace Fsgg
module Schemas =
    let governanceHandoffContractVersion = "1.1.0"
module Mirror =
    let governanceHandoffContractVersion = "2.0.0"
FS
must_fail "red: constant bound twice with DIFFERENT values" "more than once" "$GOOD_REG" "$DUP_SRC"

# An absent subject is not a coherent one.
ABSENT_REG="$WORK/absent.yml"
cat > "$ABSENT_REG" <<'YAML'
schemaVersion: 1
contracts:
  - { id: shared-build-config, version: "1.0.0" }
YAML
must_fail "red: governance-handoff absent from the registry" "absent" "$ABSENT_REG" "$GOOD_SRC"

# The YAML float-coercion trap: unquoted `1.10` parses as the float 1.1, so a string compare would
# report drift that is a YAML artefact.
FLOAT_SRC="$(mksrc float 1.10)"
FLOAT_REG="$(mkreg float '1.10')"
must_fail 'red: unquoted `version` coerced to float' "not a quoted string|float" "$FLOAT_REG" "$FLOAT_SRC"

# A registry that is not the schema at all.
JUNK_REG="$WORK/junk.yml"
printf 'just a string\n' > "$JUNK_REG"
must_fail "red: registry is not a mapping" "not a mapping" "$JUNK_REG" "$GOOD_SRC"

# ---- the gate must be pointed at a subject at all ---------------------------------------------
# `--contracts-src` is required: a gate that defaults its subject would silently check nothing.
if python3 "$GATE" "$GOOD_REG" >/dev/null 2>&1; then
  bad "--contracts-src is required (ran with no subject and reported green)"
else
  ok "--contracts-src is required"
fi

echo
echo "emitted-contract-version fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
