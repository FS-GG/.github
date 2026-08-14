#!/usr/bin/env bash
# Controlled inversion for scripts/check-prose-citations.py (.github#2587).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-prose-citations.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/prose-citations.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0 fail=0
ok() { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -z "${2:-}" ] || printf '%s\n' "$2" | sed 's/^/    | /'; fail=$((fail+1)); }
expect() {
  local name="$1" want="$2" needle="$3" root="$4" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then bad "$name (missing '$needle')" "$out"
  else ok "$name"; fi
}
fixture() {
  local root="$1"
  mkdir -p "$root/docs/guide" "$root/scripts"
  git -C "$root" init -q
  git -C "$root" config user.email fixture@example.invalid
  git -C "$root" config user.name fixture
  printf '# live\n\nSee `scripts/live.py:1`.\n' > "$root/docs/guide/live.md"
  printf 'print("live")\n' > "$root/scripts/live.py"
  git -C "$root" add docs/guide/live.md scripts/live.py
}

CLEAN="$WORK/clean"; fixture "$CLEAN"
expect "clean non-empty local citation corpus is green" 0 "1 local citations" "$CLEAN"

BROKEN="$WORK/broken"; cp -a "$CLEAN" "$BROKEN"
printf '\nSee `scripts/removed-check.py:10`.\n' >> "$BROKEN/docs/guide/live.md"
git -C "$BROKEN" add docs/guide/live.md
expect "a repository-local citation to an untracked file is red" 1 \
  "scripts/removed-check.py:10 does not name a tracked file" "$BROKEN"

UNTRACKED="$WORK/untracked"; cp -a "$CLEAN" "$UNTRACKED"
printf 'print("not committed")\n' > "$UNTRACKED/scripts/untracked.py"
printf '\nSee `scripts/untracked.py:1`.\n' >> "$UNTRACKED/docs/guide/live.md"
git -C "$UNTRACKED" add docs/guide/live.md
expect "an existing but untracked target is still red" 1 "scripts/untracked.py:1" "$UNTRACKED"

EXTERNAL="$WORK/external"; fixture "$EXTERNAL"
cat >> "$EXTERNAL/docs/guide/live.md" <<'EOF'
External references are `HandlersEvidence.fs:12`, `src/Canvas/SpatialGrid.fs:101`, and
`FS-GG/FS.GG.SDD@abc123:src/FS.GG.Contracts/Schemas.fs:185`.
EOF
git -C "$EXTERNAL" add docs/guide/live.md
expect "bare basenames, foreign source namespaces, and qualified repo citations are ignored" \
  0 "1 local citations" "$EXTERNAL"

OTHER_ROOT="$WORK/other-root"; fixture "$OTHER_ROOT"
mkdir -p "$OTHER_ROOT/.fsgg"
printf '# policy\n\nSee `scripts/live.py:1` and `.fsgg/removed.yml:1`.\n' > "$OTHER_ROOT/.fsgg/policy.md"
git -C "$OTHER_ROOT" add .fsgg/policy.md
expect "a tracked Markdown root outside docs is audited, and its missing local target is red" \
  1 ".fsgg/removed.yml:1 does not name a tracked file" "$OTHER_ROOT"

HISTORY="$WORK/history"; fixture "$HISTORY"
mkdir -p "$HISTORY/docs/adr" "$HISTORY/docs/reports"
printf '# old decision\n\n`scripts/gone.py:4`\n' > "$HISTORY/docs/adr/0001-old.md"
printf '# dated report\n\n`scripts/gone.py:4`\n' > "$HISTORY/docs/reports/2020-01-01-old.md"
git -C "$HISTORY" add docs/adr docs/reports
expect "ADRs and dated reports are historical and exempt" 0 "1 local citations" "$HISTORY"

EMPTY="$WORK/empty"; mkdir -p "$EMPTY/docs"; git -C "$EMPTY" init -q
printf '# no citation\n' > "$EMPTY/docs/readme.md"; git -C "$EMPTY" add docs/readme.md
expect "zero extracted local citations is no-verdict, never vacuous green" 3 "zero repository-local" "$EMPTY"

expect "the shipped live-document corpus is green" 0 "prose-citations: ok" "$ROOT"
echo "prose-citations fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
