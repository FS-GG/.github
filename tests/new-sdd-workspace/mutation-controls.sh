#!/usr/bin/env bash
# Gate-inversion controls for the two lifecycle decisions that ordinary positive cases can mask.
# Each case changes exactly one production expression, requires the real compiled acceptance suite
# to fail for the named reason, and restores the source byte-for-byte before continuing.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SOURCE_SUBJECT="$REPO_ROOT/scripts/NewSddWorkspace/Program.fs"
SUBJECT="$SOURCE_SUBJECT"
MODE="${FSGG_CREATOR_DEPENDENCY_MODE:-source-only}"
MUTANT_INDEX=0
WORK="$(mktemp -d "${TMPDIR:-/tmp}/new-sdd-workspace-mutations.XXXXXX")"
BACKUP="$WORK/Program.fs"
cp -p "$SOURCE_SUBJECT" "$BACKUP"
ORIGINAL_SHA="$(sha256sum "$BACKUP" | cut -d' ' -f1)"

# Preserve exact bytes but refresh mtime: otherwise MSBuild can consider the just-built mutated DLL
# newer than the restored source and let the next control execute stale mutated output.
restore() { if [ -n "$SUBJECT" ] && [ -f "$SUBJECT" ]; then cp "$BACKUP" "$SUBJECT"; fi; }
cleanup() { restore; rm -rf "$WORK"; }
trap cleanup EXIT
[ "$MODE" != published-dependency ] || SUBJECT=""

mutate_exactly_once() {
  local old="$1" new="$2"
  python3 - "$SUBJECT" "$old" "$new" <<'PY'
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
old, new = sys.argv[2:4]
text = path.read_text(encoding="utf-8")
count = text.count(old)
if count != 1:
    raise SystemExit(f"mutation subject must occur exactly once, found {count}: {old!r}")
path.write_text(text.replace(old, new), encoding="utf-8")
PY
}

expect_red() {
  local label="$1" old="$2" new="$3" needle="$4" log="$5"
  local runner="$HERE/run.sh"
  if [ "$MODE" = published-dependency ]; then
    # The positive Creator has bin/obj already. Each published mutant must start
    # from its own exact source checkout and keep the same original producer bytes.
    MUTANT_INDEX=$((MUTANT_INDEX + 1))
    local mutant="$WORK/creator-$MUTANT_INDEX"
    local source_sha
    source_sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
    git clone --no-hardlinks --no-checkout "$REPO_ROOT" "$mutant" >"$WORK/clone-$MUTANT_INDEX.log" 2>&1
    git -C "$mutant" checkout --detach "$source_sha" >>"$WORK/clone-$MUTANT_INDEX.log" 2>&1
    SUBJECT="$mutant/scripts/NewSddWorkspace/Program.fs"
    [ "$(sha256sum "$SUBJECT" | cut -d' ' -f1)" = "$ORIGINAL_SHA" ] || {
      echo "FAIL  $label — fresh mutant checkout differs from the original source"; exit 1;
    }
    runner="$mutant/tests/new-sdd-workspace/run.sh"
  elif [ "$MODE" = source-only ]; then
    restore
  else
    echo "FAIL  unknown Creator mutation dependency mode"; exit 1
  fi
  mutate_exactly_once "$old" "$new"
  # Source mode gets a fresh artifacts root; published mode keeps its explicit
  # mode/producer/dependencies/caller and builds only the new isolated Creator.
  if env -u FSGG_CREATOR_SOURCE_ARTIFACTS FSGG_MUTATION_CHILD=1 bash "$runner" >"$log" 2>&1; then
    echo "FAIL  $label — mutated suite stayed green"
    sed 's/^/    | /' "$log"
    exit 1
  fi
  if ! grep -qF "$needle" "$log"; then
    echo "FAIL  $label — suite failed, but not on the named subject"
    sed 's/^/    | /' "$log"
    exit 1
  fi
  restore
  if [ "$(sha256sum "$SUBJECT" | cut -d' ' -f1)" != "$ORIGINAL_SHA" ]; then
    echo "FAIL  $label — production source was not restored exactly"
    exit 1
  fi
  if [ "$(sha256sum "$SOURCE_SUBJECT" | cut -d' ' -f1)" != "$ORIGINAL_SHA" ]; then
    echo "FAIL  $label — outer Creator source changed"; exit 1
  fi
  echo "PASS  $label"
}

expect_red \
  "wrong-default mutation is detected and restored" \
  $'Product = product\n        Template = "rendering"\n        Lifecycle = "sdd"' \
  $'Product = product\n        Template = "rendering"\n        Lifecycle = "typed-sdd"' \
  'the wizard must preserve Standard SDD as the P4 default' \
  "$WORK/wrong-default.log"

expect_red \
  "typed lifecycle-loss mutation is detected and restored" \
  'sprintf "lifecycle=%s" lifecycle' \
  'sprintf "lifecycle=%s" "sdd"' \
  'FAIL  explicit Typed SDD is forwarded unchanged' \
  "$WORK/lifecycle-loss.log"

expect_red \
  "D.5 descriptor gate mutation is detected and restored" \
  'sourceLine = Some "source: FS.GG.Workspace.Template::0.15.0"' \
  'sourceLine = Some "source: FS.GG.Workspace.Template::0.14.0"' \
  'the D.5 fable-game descriptor must select Typed SDD on omission' \
  "$WORK/d5-descriptor.log"

echo "new-sdd-workspace mutation controls — 3 passed, 0 failed"
