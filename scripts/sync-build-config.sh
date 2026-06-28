#!/usr/bin/env bash
# Distribute the FS-GG org-shared .NET build configuration into a consumer repo.
#
# Source of truth: this repo's dist/dotnet/ (Directory.Build.props, Directory.Packages.props,
# .config/dotnet-tools.json). See docs/build/README.md for the adoption model and the
# unified lockfile-restore-enforcement gate (ADR-0006).
#
# Usage:
#   scripts/sync-build-config.sh <path-to-consumer-repo>          # write/update the managed files
#   scripts/sync-build-config.sh --check <path-to-consumer-repo>  # drift check only; exit 1 on drift
#   scripts/sync-build-config.sh --adopt <path-to-consumer-repo>  # first-time: move an existing
#                                                                  # non-canonical *.props to *.local.props,
#                                                                  # then write the canonical files
#
# --check is the hook the reusable coherence workflow (.github#18) calls in every repo's CI:
# a repo that has hand-edited a managed file (or never re-synced) fails the gate.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/../dist/dotnet" && pwd)"

# Managed files, relative to the repo root.
FILES=(
  "Directory.Build.props"
  "Directory.Packages.props"
  ".config/dotnet-tools.json"
)

# A canonical synced file carries this marker; a hand-authored repo file does not.
MARKER="Source of truth: FS-GG/.github"

# XML well-formedness guard (.github#29). The drift check compares files byte-for-byte,
# so a malformed-but-verbatim source (e.g. a `--` inside an XML comment) passes --check
# yet fails every adopter's `dotnet restore`/`pack` with MSB4024. Assert the source .props
# are loadable XML BEFORE we distribute or pass-check them, so the source of truth can't
# ship invalid XML again. Prefer xmllint; fall back to python3; warn (don't block) if neither.
assert_source_xml_wellformed() {
  local validator=""
  if command -v xmllint >/dev/null 2>&1; then
    validator="xmllint"
  elif command -v python3 >/dev/null 2>&1; then
    validator="python3"
  else
    echo "WARN: no xmllint or python3 found; skipping XML well-formedness check of source .props" >&2
    return 0
  fi
  local f bad=0
  for f in "$SRC"/*.props; do
    [[ -f "$f" ]] || continue
    case "$validator" in
      xmllint) xmllint --noout "$f" || bad=1 ;;
      python3) python3 -c 'import sys,xml.dom.minidom; xml.dom.minidom.parse(sys.argv[1])' "$f" || bad=1 ;;
    esac
  done
  if [[ "$bad" -ne 0 ]]; then
    echo "Source .props in $SRC are not well-formed XML; refusing to distribute. Fix the source of truth first." >&2
    exit 1
  fi
}
assert_source_xml_wellformed

mode="apply"
case "${1:-}" in
  --check) mode="check"; shift ;;
  --adopt) mode="adopt"; shift ;;
  -*) echo "unknown flag: $1" >&2; exit 2 ;;
esac

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
  echo "usage: $(basename "$0") [--check|--adopt] <path-to-consumer-repo>" >&2
  exit 2
fi
TARGET="$(cd "$TARGET" && pwd)"

drift=0
for rel in "${FILES[@]}"; do
  src="$SRC/$rel"
  dst="$TARGET/$rel"

  case "$mode" in
    check)
      if [[ ! -f "$dst" ]]; then
        echo "DRIFT (missing): $rel"; drift=1
      elif ! diff -q "$src" "$dst" >/dev/null 2>&1; then
        echo "DRIFT (differs): $rel"; drift=1
      else
        echo "ok: $rel"
      fi
      ;;
    apply|adopt)
      mkdir -p "$(dirname "$dst")"
      # First-time adoption: a pre-existing, hand-authored .props is renamed to *.local.props
      # (the canonical file imports it), so repo-specific settings survive the takeover.
      if [[ "$rel" == *.props && -f "$dst" ]] && ! grep -q "$MARKER" "$dst"; then
        local_dst="${dst%.props}.local.props"
        if [[ "$mode" == "adopt" ]]; then
          if [[ -e "$local_dst" ]]; then
            echo "skip adopt (exists): $(basename "$local_dst") already present for $rel" >&2
          else
            mv "$dst" "$local_dst"
            echo "adopted: $rel -> $(basename "$local_dst")"
          fi
        else
          echo "REFUSING to overwrite hand-authored $rel (no '$MARKER' marker)." >&2
          echo "  Run with --adopt once to move it to $(basename "$local_dst"), or remove it first." >&2
          drift=1
          continue
        fi
      fi
      cp "$src" "$dst"
      echo "wrote: $rel"
      ;;
  esac
done

if [[ "$mode" == "check" && "$drift" -ne 0 ]]; then
  echo "Build-config drift detected. Re-run: scripts/sync-build-config.sh <repo>" >&2
  exit 1
fi
if [[ "$mode" != "check" && "$drift" -ne 0 ]]; then
  exit 1
fi
echo "Done ($mode)."
