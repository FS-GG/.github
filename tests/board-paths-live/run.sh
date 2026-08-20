#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d "$here/.tmp.XXXXXX")"
trap 'rm -rf "$work"' EXIT
baseline_rel="${work#"$root/"}/exact/baseline.txt"
mkdir -p "$work/exact"
cat >"$work/exact/baseline.txt" <<'BASELINE'
# THIS FILE ONLY EVER SHRINKS; counts match EXACTLY.
# board-paths-live: maintaining-issues=.github#42
1 scripts/example.sh
BASELINE

cat >"$work/green.json" <<JSON
[{"number":1,"state":"open","body":"Paths: scripts/new-planned.py"},{"number":42,"state":"OPEN","body":"Paths: $baseline_rel"}]
JSON
out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/green.json" --baseline-root "$work")"
grep -q '0 coherence violation(s)' <<<"$out"

cat >"$work/moved.json" <<'JSON'
[{"number":2,"state":"OPEN","body":"Paths: src/FS.GG.Coord.Cli/Options.fs"}]
JSON
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/moved.json" --baseline-root "$work" 2>&1)"; then
  echo 'fixture must reject a token whose live destination is proven by rename history' >&2; exit 1
fi
grep -q '#2 declares moved-away token src/FS.GG.Coord.Cli/Options.fs' <<<"$out"
grep -q 'src/FS.GG.Coord.Cli.Kernel/Options.fs' <<<"$out"

cat >"$work/gap.json" <<'JSON'
[{"number":42,"state":"OPEN","body":"Paths: scripts/example.sh"}]
JSON
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/gap.json" --baseline-root "$work" 2>&1)"; then
  echo 'fixture must reject a maintaining issue whose Paths omit its exact-count baseline' >&2; exit 1
fi
grep -q '#42 maintains .*baseline.txt but its Paths declaration does not cover' <<<"$out"

cat >"$work/retired.json" <<'JSON'
[{"number":3,"state":"OPEN","body":"Paths: .codex/skills/pnext-item"}]
JSON
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/retired.json" --baseline-root "$work" 2>&1)"; then
  echo 'fixture must reject an explicitly retired root' >&2; exit 1
fi
grep -q '#3 declares retired root token .codex/skills/pnext-item' <<<"$out"

printf 'not json' >"$work/bad.json"
if python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/bad.json" --baseline-root "$work" >/dev/null 2>&1; then
  echo 'fixture must reject unreadable board evidence' >&2; exit 1
fi
echo 'board-paths-live fixture: OK'
