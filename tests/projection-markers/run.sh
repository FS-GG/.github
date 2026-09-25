#!/usr/bin/env bash
# Exercise the live projection producer against disposable target copies.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
engine="${FSGG_COORD_ENGINE_BIN:?set FSGG_COORD_ENGINE_BIN to a built coordination engine}"
work="$(mktemp -d "${TMPDIR:-/tmp}/projection-markers.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/tree/scripts"
cp "$root/scripts/generate-projections" "$work/tree/scripts/"
cp -a "$root/docs" "$root/registry" "$root/.claude" "$root/.agents" "$work/tree/"
target="$work/tree/docs/registry/compatibility.md"
original="$work/original.md"
cp "$target" "$original"
catalog="$work/tree/registry/skills.yml"
original_catalog="$work/original-skills.yml"
cp "$catalog" "$original_catalog"
producer="$work/tree/scripts/generate-projections"

run_gate() {
  FSGG_COORD_ENGINE_BIN="$engine" "$producer" --check >"$work/out" 2>&1
}
expect_refusal() {
  local label="$1" needle="$2" rc=0
  run_gate || rc=$?
  if [ "$rc" -ne 2 ] || ! grep -Fq "$needle" "$work/out"; then
    echo "FAIL $label: expected exit 2 and '$needle', got $rc" >&2
    cat "$work/out" >&2
    exit 1
  fi
  echo "PASS $label"
}

run_gate
echo "PASS unmodified source and targets"

python3 - "$target" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text()
start_marker = '<!-- BEGIN GENERATED: fsgg-skill-registry-counts -->'
end_marker = '<!-- END GENERATED: fsgg-skill-registry-counts -->'
start = text.index(start_marker)
end = text.index(end_marker, start) + len(end_marker)
path.write_text(text + '\n' + text[start:end] + '\n')
PY
expect_refusal "duplicate complete generated region" "exactly one BEGIN and one END marker"
before="$(sha256sum "$target" | cut -d' ' -f1)"
rc=0
FSGG_COORD_ENGINE_BIN="$engine" "$producer" >"$work/out" 2>&1 || rc=$?
after="$(sha256sum "$target" | cut -d' ' -f1)"
if [ "$rc" -ne 2 ] || [ "$before" != "$after" ]; then
  echo "FAIL duplicate marker write-mode refusal changed the target or returned $rc" >&2
  cat "$work/out" >&2
  exit 1
fi
echo "PASS duplicate marker refuses write without changing target"

cp "$original" "$target"
python3 - "$target" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text()
start = '<!-- BEGIN GENERATED: fsgg-skill-registry-counts -->'
end = '<!-- END GENERATED: fsgg-skill-registry-counts -->'
path.write_text(text.replace(start, 'TEMP-MARKER', 1).replace(end, start, 1).replace('TEMP-MARKER', end, 1))
PY
expect_refusal "reversed marker order" "END before BEGIN"

cp "$original" "$target"
python3 - "$target" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text()
end = '<!-- END GENERATED: fsgg-skill-registry-counts -->'
path.write_text(text.replace(end, '', 1))
PY
expect_refusal "missing end marker" "never closes it"

cp "$original" "$target"
run_gate
echo "PASS restored target"

mutate_catalog() {
  local case_name="$1"
  cp "$original_catalog" "$catalog"
  python3 - "$catalog" "$case_name" <<'PY'
import json
from pathlib import Path
import sys
import yaml
path = Path(sys.argv[1])
kind = sys.argv[2]
value = yaml.safe_load(path.read_text())
if kind == "duplicate-id":
    value["skills"].append(dict(value["skills"][0]))
elif kind == "empty":
    value["skills"] = []
elif kind == "foreign-scope":
    value["skills"][0]["scope"] = "foreign"
elif kind == "malformed-scope":
    value["skills"][0]["scope"] = []
elif kind == "markdown-owner":
    value["skills"][0]["owner"] = "owner` | 99 |\n| forged"
elif kind == "missing-owner":
    del value["skills"][0]["owner"]
elif kind == "reverse":
    value["skills"].reverse()
else:
    raise AssertionError(kind)
path.write_text(json.dumps(value))
PY
}

for case_name in duplicate-id empty foreign-scope malformed-scope markdown-owner missing-owner; do
  mutate_catalog "$case_name"
  case "$case_name" in
    duplicate-id) reason="repeats id" ;;
    empty) reason="nonempty skills list" ;;
    foreign-scope|malformed-scope) reason="unsupported scope" ;;
    markdown-owner|missing-owner) reason="unsafe or missing owner" ;;
  esac
  expect_refusal "catalog $case_name" "$reason"
done

cp "$original_catalog" "$catalog"
python3 - "$catalog" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
path.write_text('schemaVersion: 3\n' + path.read_text())
PY
expect_refusal "duplicate YAML root key" "duplicate YAML key"

mutate_catalog reverse
run_gate
echo "PASS source row order does not change generated counts"

cp "$original_catalog" "$catalog"
cp "$original" "$work/external-target.md"
rm "$target"
ln -s "$work/external-target.md" "$target"
expect_refusal "symlinked projection target" "crosses a symlink"
rm "$target"
cp "$original" "$target"

cp "$original_catalog" "$work/external-skills.yml"
rm "$catalog"
ln -s "$work/external-skills.yml" "$catalog"
expect_refusal "symlinked projection source" "crosses a symlink"
