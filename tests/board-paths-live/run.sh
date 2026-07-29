#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
cat >"$work/items.json" <<'JSON'
[{"number":1,"state":"OPEN","body":"Paths: scripts/new-planned.py"},{"number":2,"state":"OPEN","body":"Paths: .claude/skills/check-board"},{"number":3,"state":"OPEN","body":"Paths: .codex/skills/pnext-item"}]
JSON
out="$(python3 "$here/../../scripts/check-board-paths-live.py" --items "$work/items.json")"
grep -q '#3 declares retired root token .codex/skills/pnext-item' <<<"$out"
grep -q '1 retired-root token(s)' <<<"$out"
! grep -q 'new-planned\|check-board' <<<"$out"
echo 'board-paths-live fixture: OK'
