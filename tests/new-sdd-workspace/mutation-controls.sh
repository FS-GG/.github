#!/usr/bin/env bash
# Gate-inversion controls for the two lifecycle decisions that ordinary positive cases can mask.
# Each case changes exactly one production expression, requires the real compiled acceptance suite
# to fail for the named reason, and restores the source byte-for-byte before continuing.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SUBJECT="$REPO_ROOT/scripts/NewSddWorkspace/Program.fs"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/new-sdd-workspace-mutations.XXXXXX")"
BACKUP="$WORK/Program.fs"
cp -p "$SUBJECT" "$BACKUP"
ORIGINAL_SHA="$(sha256sum "$BACKUP" | cut -d' ' -f1)"

# Preserve exact bytes but refresh mtime: otherwise MSBuild can consider the just-built mutated DLL
# newer than the restored source and let the next control execute stale mutated output.
restore() { cp "$BACKUP" "$SUBJECT"; }
cleanup() { restore; rm -rf "$WORK"; }
trap cleanup EXIT

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
  restore
  mutate_exactly_once "$old" "$new"
  if FSGG_MUTATION_CHILD=1 bash "$HERE/run.sh" >"$log" 2>&1; then
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
  echo "PASS  $label"
}

expect_red \
  "wrong-default mutation is detected and restored" \
  'Lifecycle = "sdd"' \
  'Lifecycle = "typed-sdd"' \
  'FAIL  omitted lifecycle forwards the Standard SDD default' \
  "$WORK/wrong-default.log"

expect_red \
  "typed lifecycle-loss mutation is detected and restored" \
  'sprintf "lifecycle=%s" opts.Lifecycle' \
  'sprintf "lifecycle=%s" "sdd"' \
  'FAIL  explicit Typed SDD is forwarded unchanged' \
  "$WORK/lifecycle-loss.log"

echo "new-sdd-workspace mutation controls — 2 passed, 0 failed"
