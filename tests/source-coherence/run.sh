#!/usr/bin/env bash
# Fixture for scripts/check-source-coherence.py — the gate that compares the registry's
# `fsgg-contracts` version against FS.GG.Contracts' SOURCE on FS.GG.SDD@main (.github#741, epic #266).
#
# The gate exists because a check that passes when its subject is missing manufactures confidence.
# So this fixture spends most of its length on the FAILURE legs: it proves the gate goes red when
# the registry disagrees with the source, when either file is unreadable, when a version literal is
# missing or duplicated, when the Contracts tree contradicts itself, when the fsgg-contracts row is
# absent, and when YAML has coerced an unquoted `version` to a float.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than
# from the thing under test. `must_fail` therefore takes a required pattern.
#
# Throwaway trees under a temp dir. NO NETWORK: the gate reads a checked-out source tree from a
# path, which is why the workflow does the checkout and the gate does not. Mirrors
# tests/feed-coherence/run.sh.

set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-source-coherence.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/source-coherence-fixture.XXXXXX")"
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

# ---- the GREEN baseline: registry agrees with a coherent source tree -------------------------

mksrc() {  # mksrc <dir> <fsproj-version> <contractversion-value>
  local d="$WORK/$1"; mkdir -p "$d"
  cat > "$d/FS.GG.Contracts.fsproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>$2</Version>
  </PropertyGroup>
</Project>
XML
  cat > "$d/ContractVersion.fs" <<FS
module Fsgg.ContractVersion
let value = "$3"
FS
  echo "$d"
}

mkreg() {  # mkreg <name> <yaml-version-literal>
  local f="$WORK/$1.yml"
  cat > "$f" <<YAML
schemaVersion: 1
updated: "2026-07-16"
contracts:
  - { id: fsgg-contracts, version: $2, package-version: "2.0.1" }
  - { id: shared-build-config, version: "1.0.0" }
YAML
  echo "$f"
}

GOOD_SRC="$(mksrc good 2.0.1 2.0.1)"
GOOD_REG="$(mkreg good '"2.0.1"')"

must_pass "green: registry version == source version" "$GOOD_REG" "$GOOD_SRC"

# ---- the assertion itself ---------------------------------------------------------------------

# The real .github#741 / FS.GG.SDD#432 scenario: SDD bumped its source, the registry has not been
# flipped. This MUST be red — and red HERE, in the repo that owns the registry, not in six others.
BUMPED_SRC="$(mksrc bumped 2.1.0 2.1.0)"
must_fail "red: source ahead of registry (the publish-before-flip window)" \
  "version.*'2\.0\.1'.*SOURCE.*'2\.1\.0'|is '2\.0\.1' but the" "$GOOD_REG" "$BUMPED_SRC"

# The other direction: registry advertises a version SDD's source does not have.
AHEAD_REG="$(mkreg ahead '"9.9.9"')"
must_fail "red: registry ahead of source" "9\.9\.9" "$AHEAD_REG" "$GOOD_SRC"

# ---- fails-open conditions: every one of these must be an ERROR, never a skip -----------------

# The SDD 1.4.1 incoherence, which red every .github PR: fsproj moved, the constant did not.
SPLIT_SRC="$(mksrc split 1.4.1 1.4.0)"
must_fail "red: Contracts tree contradicts itself (fsproj != ContractVersion)" \
  "internally incoherent" "$GOOD_REG" "$SPLIT_SRC"

# An unreadable subject is a failed read, not a coherent tree.
must_fail "red: source tree absent" "cannot read|FAILED READ" "$GOOD_REG" "$WORK/nonexistent-dir"

must_fail "red: registry absent" "cannot read|FAILED READ" "$WORK/nonexistent.yml" "$GOOD_SRC"

# A source file present but carrying no version literal.
NOVER_SRC="$(mksrc nover 2.0.1 2.0.1)"
cat > "$NOVER_SRC/FS.GG.Contracts.fsproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
XML
must_fail "red: fsproj carries no <Version>" "could not find" "$GOOD_REG" "$NOVER_SRC"

# Two DIFFERENT <Version> tags: the file's shape is not what the gate believes. Taking the first
# would be a guess wearing a confident number.
DUP_SRC="$(mksrc dup 2.0.1 2.0.1)"
cat > "$DUP_SRC/FS.GG.Contracts.fsproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><Version>2.0.1</Version></PropertyGroup>
  <PropertyGroup><Version>3.0.0</Version></PropertyGroup>
</Project>
XML
must_fail "red: fsproj declares two DIFFERENT <Version> values" "more than once" "$GOOD_REG" "$DUP_SRC"

# An absent subject is not a coherent one.
ABSENT_REG="$WORK/absent.yml"
cat > "$ABSENT_REG" <<'YAML'
schemaVersion: 1
contracts:
  - { id: shared-build-config, version: "1.0.0" }
YAML
must_fail "red: fsgg-contracts absent from the registry" "absent" "$ABSENT_REG" "$GOOD_SRC"

# The YAML float-coercion trap that `package-version` is quoted to avoid: unquoted `1.10` parses as
# the float 1.1, so a string compare would report drift that is a YAML artefact.
FLOAT_SRC="$(mksrc float 1.10 1.10)"
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
echo "source-coherence fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
