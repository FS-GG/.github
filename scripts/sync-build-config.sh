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
#                                                                  # non-canonical *.props to *.local.props
#                                                                  # (imported) and a hand-authored
#                                                                  # dotnet-tools.json to *.local.json
#                                                                  # (backup only), then write the
#                                                                  # canonical files
#
# Hand-authored-file safety (.github#387): a *.props carries a marker, so apply REFUSES to overwrite
# an unmarked one (run --adopt to move it to an imported *.local.props). The tool manifest is JSON
# with no marker and no *.local override, and re-sync overwrites it by design — so apply only WARNS
# before a content-changing overwrite, while --adopt keeps a *.local.json backup (not re-imported).
#
# --check is the hook the reusable coherence workflow (.github#18) calls in every repo's CI:
# a repo that has hand-edited a managed file (or never re-synced) fails the gate.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/../dist/dotnet" && pwd)"

# Absolute, resolved path to this script. The --check failure message must name a command the reader
# can actually RUN, and the reader is almost never standing where this script lives (.github#633):
# every receiver's CI checks this repo out into a scratch dir and invokes it against a separate
# checkout of the repo under test, so "scripts/sync-build-config.sh" is No such file or directory in
# the repo whose gate just went red. $0 alone is not enough either — it is relative to the invoking
# cwd, so it stops resolving the moment the reader cd's anywhere.
SELF="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"

# Managed files, relative to the repo root.
#
# ADDING A FILE HERE MERGE-FREEZES EVERY RECEIVER THAT DOES NOT ALREADY HAVE IT. `--check` treats a
# missing managed file as DRIFT (see the `check` arm below), and the drift check is a REQUIRED check
# in adopting repos. So the moment a name lands in this list, every repo without that file goes red
# on a check it cannot make green from its own PR — which is a merge freeze, not a nudge.
#
# This is not hypothetical. #499 moved the source of truth in Directory.Build.props and FS.GG.SDD has
# been unable to merge ANYTHING since (.github#379) — a finished, green PR sat blocked for hours. The
# rule that avoids it is the same one ADR-0032 §3 states for lock files: THE RECEIVERS ADOPT FIRST,
# and the shared config starts enforcing it LAST.
#
# So the order for any new managed file is:
#   1. add it under dist/dotnet/ (harmless: not in this list, so nothing checks it yet)
#   2. one PR per receiver, adopting it
#   3. ONLY THEN add its name here — and now the gate is asserting something already true
#
# `global.json` is at step 1 right now (.github#536). It is in dist/dotnet/ and DELIBERATELY not in
# this list: four of the five consumers (Game, Rendering, SDD, Governance) have no global.json at all,
# so adding it today would freeze all four. tests/sync-build-config asserts that it stays out until
# the adoption items land — a comment is not a gate (#266), so the ordering rule is a test.
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

# JSON well-formedness guard (companion to the XML guard above): the drift check compares
# .config/dotnet-tools.json byte-for-byte, so a malformed source would pass --check yet break every
# adopter's `dotnet tool restore`. Assert the source tool-manifest is valid JSON BEFORE we
# distribute it. Prefer jq; fall back to python3; warn (don't block) if neither is present.
assert_source_json_wellformed() {
  local f="$SRC/.config/dotnet-tools.json"
  [[ -f "$f" ]] || return 0
  if command -v jq >/dev/null 2>&1; then
    jq -e . "$f" >/dev/null || { echo "Source $f is not valid JSON; refusing to distribute. Fix the source of truth first." >&2; exit 1; }
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys,json; json.load(open(sys.argv[1]))' "$f" \
      || { echo "Source $f is not valid JSON; refusing to distribute. Fix the source of truth first." >&2; exit 1; }
  else
    echo "WARN: no jq or python3 found; skipping JSON well-formedness check of $f" >&2
  fi
}
assert_source_json_wellformed

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
            # Both a hand-authored $rel and a pre-existing *.local.props exist. We cannot move the
            # hand-authored file onto *.local.props (that target is taken), and falling through to the
            # canonical `cp` below would silently destroy the hand-authored file's settings. Refuse this
            # file — fail-closed, exactly like apply-mode's refusal below (.github#126, review M1). The
            # operator merges the wanted settings from $rel into the existing *.local.props, deletes
            # $rel, then re-runs --adopt.
            echo "REFUSING to adopt $rel: a hand-authored $rel and $(basename "$local_dst") both exist." >&2
            echo "  Adopting would overwrite the hand-authored $rel, but its content cannot be moved to" >&2
            echo "  $(basename "$local_dst") (already present). Merge the settings you want from $rel into" >&2
            echo "  $(basename "$local_dst"), then delete $rel and re-run --adopt." >&2
            drift=1
            continue
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
      # The tool manifest is fully managed and, unlike the .props, has NO repo-local override to
      # import into — it is distributed verbatim and OVERWRITTEN on every re-sync, which is the
      # documented update path (docs/build/README.md). Being JSON it cannot carry the $MARKER comment
      # the .props use to tell a managed file from a hand-authored one, so apply cannot refuse a
      # hand-authored manifest without also refusing a legitimately stale managed one and breaking
      # re-sync. So it must not be *silently* clobbered (.github#387): on a first-time --adopt, save
      # the existing manifest to *.local.json (a plain backup — the manifest has no include, so it is
      # NOT re-imported) before writing canonical; on a plain apply re-sync, at least WARN before an
      # overwrite that changes content. A byte-identical manifest is a quiet no-op in either mode.
      if [[ "$rel" != *.props && -f "$dst" ]] && ! diff -q "$src" "$dst" >/dev/null 2>&1; then
        local_dst="${dst%.json}.local.json"
        if [[ "$mode" == "adopt" ]]; then
          if [[ -e "$local_dst" ]]; then
            # A hand-authored $rel and a pre-existing *.local.json both exist: we cannot back $rel up
            # onto a taken target, and overwriting would lose it. Refuse — fail-closed, as the .props
            # adopt does above (.github#126).
            echo "REFUSING to adopt $rel: a hand-authored $rel and $(basename "$local_dst") both exist." >&2
            echo "  Back up or merge the tools you want from $rel, delete $(basename "$local_dst"), then re-run --adopt." >&2
            drift=1
            continue
          fi
          mv "$dst" "$local_dst"
          echo "adopted: $rel -> $(basename "$local_dst") (backup only; the manifest has no import — merge any custom tools back by hand)"
        else
          echo "WARNING: overwriting $rel, which differs from the managed source. The tool manifest is" >&2
          echo "  fully managed and has no *.local override; its previous content is replaced. If it held" >&2
          echo "  repo-specific tools, run --adopt instead (keeps a *.local.json backup) or recover from git." >&2
        fi
      fi
      cp "$src" "$dst"
      echo "wrote: $rel"
      ;;
  esac
done

if [[ "$mode" == "check" && "$drift" -ne 0 ]]; then
  # Name a runnable command, not a relative path that exists only in .github (#633). Both forms are
  # printed because there are two readers of this failure and they are standing in different places:
  # someone with a .github checkout (the resolved path works), and someone looking at a red gate in a
  # receiver repo, who has no .github checkout at all (the clone form works).
  #
  # %q, not bare interpolation: these lines exist to be PASTED. An unquoted path with a space in it
  # would paste as two arguments and run the sync against the wrong directory. %q leaves an ordinary
  # path untouched, so this costs nothing in the normal case.
  #
  # The clone form clones into a FRESH mktemp dir rather than a fixed /tmp/... path, because a fixed
  # one makes the command fail on its SECOND use ("destination path already exists and is not an empty
  # directory") — for the reader with two drifted repos, or who simply runs it twice. A remediation
  # that only works once is the same defect as one that never works.
  {
    echo ""
    echo "Build-config drift detected: the file(s) marked DRIFT above differ from the org source of truth."
    echo ""
    echo "This script lives in FS-GG/.github and is NOT checked into the repo being checked, so there is"
    echo "no 'scripts/sync-build-config.sh' in $TARGET. Re-sync with whichever fits where you are:"
    echo ""
    echo "  # ...from a checkout of FS-GG/.github (this script, resolved):"
    echo "  $(printf '%q' "$SELF") $(printf '%q' "$TARGET")"
    echo ""
    echo "  # ...from the root of the repo that just went red, with no .github checkout to hand:"
    echo "  d=\$(mktemp -d) && git clone --depth 1 https://github.com/FS-GG/.github.git \"\$d/org\" \\"
    echo "    && \"\$d/org/scripts/sync-build-config.sh\" ."
    echo ""
    echo "Then commit the updated file(s). Do not hand-edit them — they are overwritten on every re-sync."
    echo ""
    echo "If that re-sync REFUSES a file, this repo is ADOPTING one it hand-authored before the org"
    echo "managed it. Re-run the same command once with --adopt, which moves your version aside to"
    echo "*.local.props (still imported, so your settings survive) before writing the canonical file."
  } >&2
  exit 1
fi
if [[ "$mode" != "check" && "$drift" -ne 0 ]]; then
  exit 1
fi
echo "Done ($mode)."
