#!/usr/bin/env bash
# stage-kit.sh — stage the coordination kit into a directory the package packs from.
#
# The FS.GG.Kit package (ADR-0062) ships the coordination kit as ONE versioned artifact instead of
# byte-copying it into N receivers. What "the kit" IS lives in exactly one place — the `kit:` rows of
# registry/repos.yml — so this script DERIVES the packed set from that manifest at pack time and stages
# nothing that is committed (ADR-0058: derive, don't restate). There is no second list to drift.
#
#   stage-kit.sh <out-dir>
#
# It reads the same manifest scripts/coordination-sync reads, via the same reader (scripts/repos.sh
# kit), so the package and the legacy byte-copy fabric can never pack a different set while both exist.
# Layout under <out-dir> — receiver-relative destinations are recorded, not implied by the layout, so
# the materialize target (build/FS.GG.Kit.targets) is a table-driven copy, not a path convention it
# could silently diverge from:
#
#   skills/<name>/<relative file> every regular file in each `kind: skill` source directory
#   client/<name>                 one per `kind: client` row (name = basename of source)
#   config/<name>                 one per `kind: config` row (name = basename of the row's dest)
#   build-config/<rel>            one per sync-build-config.sh FILES member (the build-config capability)
#   kit-manifest.tsv              kind <TAB> package-rel <TAB> receiver dest <TAB> sha256 <TAB> executable
#
# The sha256 column is the content-addressed record, so a materialized file that does not match is a
# loud failure at restore, never a silently missing or stale file. For the coordination kit
# (skill/client/config) it is taken with the SAME digest that writes registry/repos.lock
# (scripts/repos.sh digest == sha256sum), so the two distribution paths cannot diverge (ADR-0014). The
# build-config members have NO repos.lock row — that capability uses the ADR-0036 pin model, so their
# sha256 is a self-consistent integrity record only, and "behind" is a version-pin decision (which
# FS.GG.Kit a receiver references), not drift.
#
# Exit: 0 staged; 2 on any misconfiguration (a manifest that does not parse, a source that is missing,
# a registry read that FAILS, or a staged manifest that does not carry the coordination kit — #1614).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "$HERE/../.." && pwd)"          # the .github checkout = canonical kit source
REG="$SRC_ROOT/registry/repos.yml"
REPOS="$SRC_ROOT/scripts/repos.sh"

die() { echo "stage-kit: $*" >&2; exit 2; }

[ $# -eq 1 ] || die "usage: stage-kit.sh <out-dir>"
OUT="$1"
[ -f "$REG" ]   || die "registry not found at $REG (is this a .github checkout?)"
[ -x "$REPOS" ] || [ -f "$REPOS" ] || die "scripts/repos.sh not found at $REPOS"

# Fresh staging every run — the derived set must never carry a file a prior manifest named and this
# one does not (the exact staleness a byte-copy fabric suffers, reintroduced into the producer).
rm -rf "$OUT"
mkdir -p "$OUT"
MANIFEST="$OUT/kit-manifest.tsv"
: > "$MANIFEST"

# digest: parity with registry/repos.lock. repos.sh digest emits bare sha256 hex.
digest() { bash "$REPOS" digest "$1"; }

read_kit() { bash "$REPOS" kit --field "$1" --kind "$2" --registry "$REG"; }

# CAPTURE each registry read into a variable and CHECK its exit code before consuming it (#1614).
#
# This script used to consume the reader through a process substitution — `done < <(read_kit …)`, and
# `paste <(…) <(…) | while …` for the config pair. `set -euo pipefail` does not see a failure inside
# `<(...)`, so a repos.sh that DIES (an unreadable registry, an absent/broken yq, a repos.sh
# regression) yielded an EMPTY list, every loop body ran zero times, and the kit staged with no skills,
# no fsgg-coord client and no engine manifest — reporting success, because the only floor downstream
# was `[ -s "$MANIFEST" ]` and the build-config rows (derived separately, from sync-build-config.sh)
# keep that satisfied on their own.
#
# "I could not ask the registry" is not "the kit has no skills". This is the same fail-closed discipline
# scripts/coordination-sync applies to the identical reads, which this script's own comments already
# claimed and did not follow. Reading into a variable ALSO moves every consuming loop into the main
# shell (`done <<< "$VAR"`), so a `die` inside one exits the script rather than a subshell.
#
# The reader below assigns INDIRECTLY rather than returning on stdout, for the same reason: a
# `X="$(read_kit_checked …)"` shape would put the read — including its `die` — inside a command
# substitution, where `exit 2` kills only that subshell and the refusal reaches the script solely as an
# exit status `set -e` has to notice. A FAIL-CLOSED path that depends on `set -e` propagating out of a
# subshell is the same class of mistake as the process substitution it replaces.
KIT_SKILL_SRCS=""; KIT_CLIENT_SRCS=""; KIT_CONFIG_SRCS=""; KIT_CONFIG_DSTS=""
read_kit_into() {   # read_kit_into <var> <field> <kind> <what-for-the-message>
  local __var="$1" out rc=0
  out="$(read_kit "$2" "$3")" || rc=$?
  [ "$rc" -eq 0 ] || die "could not read the kit $4 from $REG (repos.sh exited $rc)"
  printf -v "$__var" '%s' "$out"
}

# An EMPTY skill or client list is a failure, not an empty loop: the coordination kit is defined by
# ADR-0062 to carry both, so "the registry declares none" is a broken registry, not a smaller kit.
# Config stays optional (a kit may legitimately declare none), exactly as coordination-sync has it.
read_kit_into KIT_SKILL_SRCS  source skill  'skill list'
[ -n "$KIT_SKILL_SRCS" ]  || die "no 'kind: skill' kit item declared in $REG — the coordination kit requires at least one"
read_kit_into KIT_CLIENT_SRCS source client 'client list'
[ -n "$KIT_CLIENT_SRCS" ] || die "no 'kind: client' kit item declared in $REG — the coordination kit requires at least one"
read_kit_into KIT_CONFIG_SRCS source config 'config sources'
read_kit_into KIT_CONFIG_DSTS dest   config 'config dests'

# --- skills: complete source directory -> skills/<name>/ ; materialized at every root/<name>/ ---
while IFS= read -r src; do
  [ -n "$src" ] || continue
  name="${src##*/}"
  [ -f "$SRC_ROOT/$src/SKILL.md" ] || die "canonical kit skill source missing: $src/SKILL.md"
  find "$SRC_ROOT/$src" -mindepth 1 ! -type d ! -type f -print -quit | grep -q . \
    && die "canonical kit skill source contains a non-regular file: $src"
  staged=0
  while IFS= read -r -d '' from; do
    rel="${from#"$SRC_ROOT/$src/"}"
    mkdir -p "$OUT/skills/$name/$(dirname "$rel")"
    cp "$from" "$OUT/skills/$name/$rel"
    [ -x "$from" ] && executable=true || executable=false
    printf 'skill\tskills/%s/%s\t%s/%s\t%s\t%s\n' \
      "$name" "$rel" "$name" "$rel" "$(digest "$from")" "$executable" >> "$MANIFEST"
    staged=$((staged + 1))
  done < <(find "$SRC_ROOT/$src" -type f -print0 | LC_ALL=C sort -z)
  # The enumeration is a process substitution too, so its failure is swallowed the same way. SKILL.md
  # was just asserted to exist, so a skill that staged NOTHING means the walk did not run — not that
  # the skill is empty. Fail closed rather than pack a named skill with no files.
  [ "$staged" -gt 0 ] \
    || die "staged no file for kit skill '$src' though $src/SKILL.md exists — the source walk failed"
done <<< "$KIT_SKILL_SRCS"

# --- client: <source> -> client/<name> ; materialized at scripts/<name> (executable) ---
while IFS= read -r src; do
  [ -n "$src" ] || continue
  name="${src##*/}"
  from="$SRC_ROOT/$src"
  [ -f "$from" ] || die "canonical kit client source missing: $src"
  mkdir -p "$OUT/client"
  cp "$from" "$OUT/client/$name"
  printf 'client\tclient/%s\tscripts/%s\t%s\ttrue\n' "$name" "$name" "$(digest "$from")" >> "$MANIFEST"
done <<< "$KIT_CLIENT_SRCS"

# --- config: a `kind: config` row NAMES its own receiver dest (source path != dest) ---
# jq emits both fields in `.kit` order, so paste aligns them row-for-row; the printf process
# substitutions below cannot fail, so the fail-closed reads above stand.
KIT_CONFIG_PAIRS=""
[ -z "$KIT_CONFIG_SRCS" ] \
  || KIT_CONFIG_PAIRS="$(paste <(printf '%s\n' "$KIT_CONFIG_SRCS") <(printf '%s\n' "$KIT_CONFIG_DSTS"))"
config_rows=0
[ -z "$KIT_CONFIG_PAIRS" ] || while IFS=$'\t' read -r src dest; do
  [ -n "$src" ] || continue
  case "$dest" in ""|null) die "kit config source '$src' declares no dest (run: scripts/repos.sh validate)" ;; esac
  name="${dest##*/}"
  from="$SRC_ROOT/$src"
  [ -f "$from" ] || die "canonical kit config source missing: $src"
  mkdir -p "$OUT/config"
  cp "$from" "$OUT/config/$name"
  [ -x "$from" ] && executable=true || executable=false
  printf 'config\tconfig/%s\t%s\t%s\t%s\n' "$name" "$dest" "$(digest "$from")" "$executable" >> "$MANIFEST"
  config_rows=$((config_rows + 1))
done <<< "$KIT_CONFIG_PAIRS"

# --- build-config: the sync-build-config.sh FILES set, materialized at the receiver ROOT (opt-in) ---
# DERIVE the byte-identity set from sync-build-config.sh (ADR-0058), rather than restating it here — the
# same FILES it distributes. global.json is DELIBERATELY not in that list (.github#903: per-repo SDK
# bands are legitimate), so it is not carried either. `.config/dotnet-tools.json` is NOT here — it moved
# to the coordination kit as the engine manifest (#1077) and is staged above as a `config` row.
SBC="$SRC_ROOT/scripts/sync-build-config.sh"
[ -f "$SBC" ] || die "sync-build-config.sh not found at $SBC (is this a .github checkout?)"
# Extract the FILES=(...) array body: drop the delimiters, strip comments / quotes / whitespace, and
# read one member per line (mapfile, not word-splitting — SC2207).
mapfile -t bc_files < <(sed -n '/^FILES=(/,/^)/{/^FILES=(/d;/^)/d;s/#.*//;s/[[:space:]"]//g;/^$/d;p}' "$SBC")
[ "${#bc_files[@]}" -gt 0 ] || die "could not derive the build-config FILES set from $SBC (its FILES=(...) shape changed?)"
for rel in "${bc_files[@]}"; do
  from="$SRC_ROOT/dist/dotnet/$rel"
  [ -f "$from" ] || die "canonical build-config source missing: dist/dotnet/$rel"
  mkdir -p "$OUT/build-config/$(dirname "$rel")"
  cp "$from" "$OUT/build-config/$rel"
  [ -x "$from" ] && executable=true || executable=false
  printf 'build-config\tbuild-config/%s\t%s\t%s\t%s\n' "$rel" "$rel" "$(digest "$from")" "$executable" >> "$MANIFEST"
done

# --- the floor: the manifest must carry the COORDINATION KIT, not merely SOME row (#1614) ----------
# `[ -s "$MANIFEST" ]` alone was satisfiable by the build-config rows, which are derived from
# sync-build-config.sh rather than from the registry — so it stayed green on a run in which EVERY
# registry-derived read returned nothing. Assert on what was actually written, per kind: this is a
# check on the staged artifact, independent of the reads above, so it still catches a future
# regression that reintroduces the swallow somewhere else.
[ -s "$MANIFEST" ] || die "manifest is empty — the kit reader returned no rows (run: scripts/repos.sh validate)"
rows_of() { grep -c "^$1$(printf '\t')" "$MANIFEST" || true; }
[ "$(rows_of skill)" -gt 0 ] \
  || die "staged manifest carries no 'skill' row — the coordination kit is not in this package"
[ "$(rows_of client)" -gt 0 ] \
  || die "staged manifest carries no 'client' row — the coordination kit is not in this package"
# Every config row the registry DECLARED must have been staged: a `config` kind that read fine and then
# staged nothing would otherwise pass the two floors above on the strength of skills alone.
declared_config=0
[ -z "$KIT_CONFIG_SRCS" ] || declared_config="$(printf '%s\n' "$KIT_CONFIG_SRCS" | grep -c .)"
[ "$config_rows" -eq "$declared_config" ] \
  || die "the registry declares $declared_config kit config row(s) but $config_rows staged"
echo "stage-kit: staged $(wc -l < "$MANIFEST") kit file(s) into $OUT"
