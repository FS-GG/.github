#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
cat >"$work/items.json" <<'JSON'
[{"number":1,"state":"OPEN","body":"Paths: scripts/new-planned.py"},{"number":2,"state":"OPEN","body":"Paths: .claude/skills/check-board"},{"number":3,"state":"OPEN","body":"Paths: .codex/skills/pnext-item"}]
JSON
out="$(python3 "$here/../../scripts/check-board-paths-live.py" --items "$work/items.json")"
grep -q '#3 declares retired root token .codex/skills/pnext-item' <<<"$out"
grep -q '1 retired-root token(s)' <<<"$out"
# NOT `! grep -q …`. Bash exempts a `!`-inverted command from errexit, so that spelling ran, printed
# nothing, and could not fail: the one leg here that refuses a FALSE POSITIVE was decorative from the
# day it landed (.github#2556 review round 1). A detector that started naming live, non-retired roots
# — the regression this leg exists to catch — left the fixture printing OK and exiting 0.
if grep -q 'new-planned\|check-board' <<<"$out"; then
  echo 'fixture must not report a live (non-retired) declared root' >&2; exit 1
fi
printf 'not json' >"$work/bad.json"
if python3 "$here/../../scripts/check-board-paths-live.py" --items "$work/bad.json" >/dev/null 2>&1; then
  echo 'fixture must reject unreadable board evidence' >&2; exit 1
fi
echo 'board-paths-live fixture: OK'
