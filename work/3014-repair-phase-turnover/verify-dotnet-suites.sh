#!/usr/bin/env bash
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
out_dir="${1:-$root/work/3014-repair-phase-turnover/test-results}"
raw_dir="$(mktemp -d)"
trap 'rm -rf "$raw_dir"' EXIT

cd "$root"
dotnet restore tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj --locked-mode
dotnet restore tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj --locked-mode
dotnet test tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj \
  -c Release --no-restore --results-directory "$raw_dir/core" \
  --logger 'trx;LogFileName=core.trx'
dotnet test tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj \
  -c Release --no-restore --results-directory "$raw_dir/lifecycle" \
  --logger 'trx;LogFileName=lifecycle.trx'

mkdir -p "$out_dir"
python3 - "$raw_dir/core/core.trx" "$out_dir/core.junit.xml" \
  934 'FS.GG.Coord.Core full regression' \
  'dotnet test tests/FS.GG.Coord.Core.Tests -c Release --no-restore' <<'PY'
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr

source, output, expected_text, suite, command = sys.argv[1:]
expected = int(expected_text)
counters = next(element for element in ET.parse(source).iter() if element.tag.endswith("Counters"))
passed = int(counters.attrib["passed"])
failed = int(counters.attrib["failed"])
total = int(counters.attrib["total"])
if (total, passed, failed) != (expected, expected, 0):
    raise SystemExit(f"unexpected TRX counters: total={total} passed={passed} failed={failed}")
xml = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    f'<testsuite name={quoteattr(suite)} tests="{expected}" failures="0" errors="0" skipped="0">\n'
    '  <properties>\n'
    f'    <property name="command" value={quoteattr(command)} />\n'
    '  </properties>\n'
    f'  <testcase classname="aggregate" name="{expected} core tests passed" />\n'
    '</testsuite>\n'
)
Path(output).write_text(xml, encoding="utf-8")
PY

python3 - "$raw_dir/lifecycle/lifecycle.trx" "$out_dir/lifecycle.junit.xml" \
  137 'FS.GG.Coord.Cli.Lifecycle full regression' \
  'dotnet test tests/FS.GG.Coord.Cli.Lifecycle.Tests -c Release --no-restore' <<'PY'
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr

source, output, expected_text, suite, command = sys.argv[1:]
expected = int(expected_text)
counters = next(element for element in ET.parse(source).iter() if element.tag.endswith("Counters"))
passed = int(counters.attrib["passed"])
failed = int(counters.attrib["failed"])
total = int(counters.attrib["total"])
if (total, passed, failed) != (expected, expected, 0):
    raise SystemExit(f"unexpected TRX counters: total={total} passed={passed} failed={failed}")
xml = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    f'<testsuite name={quoteattr(suite)} tests="{expected}" failures="0" errors="0" skipped="0">\n'
    '  <properties>\n'
    f'    <property name="command" value={quoteattr(command)} />\n'
    '  </properties>\n'
    f'  <testcase classname="aggregate" name="{expected} lifecycle tests passed" />\n'
    '</testsuite>\n'
)
Path(output).write_text(xml, encoding="utf-8")
PY

printf 'REPAIR-PHASE-TURNOVER-DOTNET-GREEN: core 934/934; lifecycle 137/137\n'
