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

# Hermetic history subject shared by the full-history, shallow-history, and pagination legs. The
# fixture never depends on how much history actions/checkout supplied for the repository under test.
history="$work/history"
git init -q -b main "$history"
git -C "$history" config user.email board-paths-live@example.invalid
git -C "$history" config user.name board-paths-live-fixture
mkdir -p "$history/src/Old"
printf 'moved\n' >"$history/src/Old/Options.fs"
git -C "$history" add src/Old/Options.fs
git -C "$history" commit -qm 'add old path'
mkdir -p "$history/src/New"
git -C "$history" mv src/Old/Options.fs src/New/Options.fs
git -C "$history" commit -qm 'move path'

cat >"$work/moved.json" <<'JSON'
[{"number":2,"state":"OPEN","body":"Paths: src/Old/Options.fs"}]
JSON
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$history" --items "$work/moved.json" --baseline-root "$work" 2>&1)"; then
  echo 'fixture must reject a token whose live destination is proven by rename history' >&2; exit 1
fi
grep -q '#2 declares moved-away token src/Old/Options.fs' <<<"$out"
grep -q 'src/New/Options.fs' <<<"$out"

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

# Production-route history control. A depth-1 clone cannot prove the rename and must leave the token
# classified as a possible planned file; fetching full history must make the identical input red.
git clone -q --depth 1 "file://$history" "$work/shallow"
mkdir -p "$work/no-baselines"
cat >"$work/shallow.json" <<'JSON'
[{"number":4,"state":"open","body":"Paths: src/Old/Options.fs"}]
JSON
[ "$(git -C "$work/shallow" rev-list --count HEAD)" -eq 1 ]
out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$work/shallow" --items "$work/shallow.json" --baseline-root "$work/no-baselines")"
grep -q '0 coherence violation(s)' <<<"$out"
git -C "$work/shallow" fetch -q --unshallow
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$work/shallow" --items "$work/shallow.json" --baseline-root "$work/no-baselines" 2>&1)"; then
  echo 'fixture must detect the moved token once the production route supplies full history' >&2; exit 1
fi
grep -q '#4 declares moved-away token src/Old/Options.fs' <<<"$out"
grep -q 'src/New/Options.fs' <<<"$out"

# Producer agreement: GitHub pagination yields an array per page, while the detector accepts one
# flat list. Prove the production jq transform preserves page two, including its red item.
cat >"$work/pages.json" <<'JSON'
[[{"number":5,"state":"open","body":"Paths: scripts/new-planned.py"}],[{"number":6,"state":"open","body":"Paths: src/Old/Options.fs"}]]
JSON
jq -c 'add' "$work/pages.json" >"$work/flattened.json"
[ "$(jq 'length' "$work/flattened.json")" -eq 2 ]
if out="$(python3 "$root/scripts/check-board-paths-live.py" --root "$history" --items "$work/flattened.json" --baseline-root "$work/no-baselines" 2>&1)"; then
  echo 'fixture must preserve and diagnose the item from page two' >&2; exit 1
fi
grep -q '#6 declares moved-away token src/Old/Options.fs' <<<"$out"
grep -q -- '--paginate --slurp' "$root/.github/workflows/board-paths-live.yml"
grep -q -- "jq 'add'" "$root/.github/workflows/board-paths-live.yml"
grep -q 'fetch-depth: 0' "$root/.github/workflows/board-paths-live.yml"

printf 'not json' >"$work/bad.json"
if python3 "$root/scripts/check-board-paths-live.py" --root "$root" --items "$work/bad.json" --baseline-root "$work" >/dev/null 2>&1; then
  echo 'fixture must reject unreadable board evidence' >&2; exit 1
fi
echo 'board-paths-live fixture: OK'
