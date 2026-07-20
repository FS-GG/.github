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
#   skills/<name>/SKILL.md        one per `kind: skill` row (name = basename of the row's source)
#   client/<name>                 one per `kind: client` row (name = basename of source)
#   config/<name>                 one per `kind: config` row (name = basename of the row's dest)
#   build-config/<rel>            one per sync-build-config.sh FILES member (the build-config capability)
#   kit-manifest.tsv              kind <TAB> package-rel path <TAB> receiver dest <TAB> sha256
#
# The sha256 column is the content-addressed record, so a materialized file that does not match is a
# loud failure at restore, never a silently missing or stale file. For the coordination kit
# (skill/client/config) it is taken with the SAME digest that writes registry/repos.lock
# (scripts/repos.sh digest == sha256sum), so the two distribution paths cannot diverge (ADR-0014). The
# build-config members have NO repos.lock row — that capability uses the ADR-0036 pin model, so their
# sha256 is a self-consistent integrity record only, and "behind" is a version-pin decision (which
# FS.GG.Kit a receiver references), not drift.
#
# Exit: 0 staged; 2 on any misconfiguration (a manifest that does not parse, a source that is missing).
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

# --- skills: <source>/SKILL.md -> skills/<name>/SKILL.md ; materialized at <root>/<name>/SKILL.md ---
while IFS= read -r src; do
  [ -n "$src" ] || continue
  name="${src##*/}"
  from="$SRC_ROOT/$src/SKILL.md"
  [ -f "$from" ] || die "canonical kit skill source missing: $src/SKILL.md"
  mkdir -p "$OUT/skills/$name"
  cp "$from" "$OUT/skills/$name/SKILL.md"
  printf 'skill\tskills/%s/SKILL.md\t%s/SKILL.md\t%s\n' "$name" "$name" "$(digest "$from")" >> "$MANIFEST"
done < <(read_kit source skill)

# --- client: <source> -> client/<name> ; materialized at scripts/<name> (executable) ---
while IFS= read -r src; do
  [ -n "$src" ] || continue
  name="${src##*/}"
  from="$SRC_ROOT/$src"
  [ -f "$from" ] || die "canonical kit client source missing: $src"
  mkdir -p "$OUT/client"
  cp "$from" "$OUT/client/$name"
  printf 'client\tclient/%s\tscripts/%s\t%s\n' "$name" "$name" "$(digest "$from")" >> "$MANIFEST"
done < <(read_kit source client)

# --- config: a `kind: config` row NAMES its own receiver dest (source path != dest) ---
paste <(read_kit source config) <(read_kit dest config) | while IFS=$'\t' read -r src dest; do
  [ -n "$src" ] || continue
  case "$dest" in ""|null) die "kit config source '$src' declares no dest (run: scripts/repos.sh validate)" ;; esac
  name="${dest##*/}"
  from="$SRC_ROOT/$src"
  [ -f "$from" ] || die "canonical kit config source missing: $src"
  mkdir -p "$OUT/config"
  cp "$from" "$OUT/config/$name"
  printf 'config\tconfig/%s\t%s\t%s\n' "$name" "$dest" "$(digest "$from")" >> "$MANIFEST"
done

# --- build-config: the sync-build-config.sh FILES set, materialized at the receiver ROOT (opt-in) ---
# DERIVE the byte-identity set from sync-build-config.sh (ADR-0058), rather than restating it here — the
# same FILES it distributes. global.json is DELIBERATELY not in that list (.github#903: per-repo SDK
# bands are legitimate), so it is not carried either. `.config/dotnet-tools.json` is NOT here — it moved
# to the coordination kit as the engine manifest (#1077) and is staged above as a `config` row.
SBC="$SRC_ROOT/scripts/sync-build-config.sh"
[ -f "$SBC" ] || die "sync-build-config.sh not found at $SBC (is this a .github checkout?)"
# Extract the FILES=(...) array body: drop the delimiters, strip comments and quotes.
bc_files=($(sed -n '/^FILES=(/,/^)/{/^FILES=(/d;/^)/d;s/#.*//;s/"//g;p}' "$SBC"))
[ "${#bc_files[@]}" -gt 0 ] || die "could not derive the build-config FILES set from $SBC (its FILES=(...) shape changed?)"
for rel in "${bc_files[@]}"; do
  from="$SRC_ROOT/dist/dotnet/$rel"
  [ -f "$from" ] || die "canonical build-config source missing: dist/dotnet/$rel"
  mkdir -p "$OUT/build-config/$(dirname "$rel")"
  cp "$from" "$OUT/build-config/$rel"
  printf 'build-config\tbuild-config/%s\t%s\t%s\n' "$rel" "$rel" "$(digest "$from")" >> "$MANIFEST"
done

[ -s "$MANIFEST" ] || die "manifest is empty — the kit reader returned no rows (run: scripts/repos.sh validate)"
echo "stage-kit: staged $(wc -l < "$MANIFEST") kit file(s) into $OUT"
