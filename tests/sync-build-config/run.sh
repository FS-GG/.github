#!/usr/bin/env bash
# Fixture for scripts/sync-build-config.sh — the org-shared .NET build-config distributor (ADR-0006).
#
# The regression under guard is .github#387: apply/adopt used to run the refuse-and-adopt safety net
# only for *.props, so a hand-authored .config/dotnet-tools.json fell through to an unconditional `cp`
# and was silently clobbered — data loss. This fixture pins that the manifest is now fail-closed the
# same way the .props files are, WITHOUT weakening the .props behaviour it was modelled on.
#
# It drives the REAL script against the REAL canonical source (dist/dotnet/) and a throwaway consumer
# dir, so it can never pass on a source that stopped shipping one of the managed files — and it never
# retypes the logic it checks. No network, no repo state beyond a mktemp'd target.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SCRIPT="$REPO_ROOT/scripts/sync-build-config.sh"
SRC="$REPO_ROOT/dist/dotnet"
MANIFEST=".config/dotnet-tools.json"
PROP="Directory.Build.props"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "sync-build-config fixture — script='$SCRIPT'"

# The source of truth must actually carry the three managed files, or every assertion below is vacuous.
for f in "$PROP" "Directory.Packages.props" "$MANIFEST"; do
  [ -f "$SRC/$f" ] || { bad "canonical source ships $f" "missing $SRC/$f"; }
done

WORK="$(mktemp -d "${TMPDIR:-/tmp}/sync-build-config-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# fresh_target -> prints a new empty consumer dir under $WORK
n=0
fresh_target() { n=$((n+1)); local t="$WORK/repo-$n"; mkdir -p "$t"; printf '%s' "$t"; }

# run <mode-flag-or-empty> <target> -> rc; captures combined output in $OUT
run() {
  local flag="$1" target="$2" rc=0
  if [ -n "$flag" ]; then OUT="$(bash "$SCRIPT" "$flag" "$target" 2>&1)" || rc=$?
  else                    OUT="$(bash "$SCRIPT"        "$target" 2>&1)" || rc=$?; fi
  return "$rc"
}

HAND='{ "version": 1, "isRoot": true, "tools": { "my-repo-tool": { "version": "1.0.0", "commands": ["mine"] } } }'

# --- apply on a clean repo writes every managed file --------------------------------------------
t="$(fresh_target)"; rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && diff -q "$SRC/$PROP" "$t/$PROP" >/dev/null 2>&1; then
  ok "apply on an empty repo writes the canonical manifest and props"
else
  bad "apply on an empty repo" "rc=$rc"$'\n'"$OUT"
fi

# --- --check is green on a freshly synced repo, red once the manifest drifts ---------------------
rc=0; run "--check" "$t" || rc=$?
[ "$rc" -eq 0 ] && ok "check is green right after a sync" || bad "check after sync" "rc=$rc"$'\n'"$OUT"

printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "--check" "$t" || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q "DRIFT (differs): $MANIFEST"; then
  ok "check reports DRIFT once the manifest is hand-edited"
else
  bad "check on a drifted manifest" "want rc!=0 + DRIFT line; rc=$rc"$'\n'"$OUT"
fi

# --- THE BUG (#387): apply must not clobber a differing manifest SILENTLY -------------------------
# The manifest is fully managed with no *.local override, so re-sync must still overwrite it (that is
# the documented update path) — refusing would break re-sync. The fix is to make the overwrite LOUD:
# apply overwrites (exit 0, so a bulk re-sync stays green) but WARNS, naming the file.
t="$(fresh_target)"; mkdir -p "$t/.config"; printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && printf '%s' "$OUT" | grep -qi "WARNING: overwriting $MANIFEST" \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1; then
  ok "apply warns (not silently) before overwriting a differing manifest, and still re-syncs it (#387)"
else
  bad "apply must warn — not silently clobber — a differing manifest (#387)" \
      "rc=$rc (want 0)"$'\n'"manifest now: $(cat "$t/$MANIFEST")"$'\n'"--- output ---"$'\n'"$OUT"
fi

# --- apply on an already-canonical manifest is a QUIET no-op (no spurious warning) ----------------
t="$(fresh_target)"; mkdir -p "$t/.config"; cp "$SRC/$MANIFEST" "$t/$MANIFEST"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && ! printf '%s' "$OUT" | grep -qi "WARNING: overwriting $MANIFEST"; then
  ok "apply is a quiet no-op on an identical manifest (no spurious overwrite warning)"
else
  bad "apply on a canonical manifest should be a quiet no-op" "rc=$rc"$'\n'"$OUT"
fi

# --- adopt backs the hand-authored manifest up to *.local.json, then writes canonical ------------
t="$(fresh_target)"; mkdir -p "$t/.config"; printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "--adopt" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && diff -q <(printf '%s\n' "$HAND") "$t/.config/dotnet-tools.local.json" >/dev/null 2>&1; then
  ok "adopt moves the hand-authored manifest to dotnet-tools.local.json and writes canonical"
else
  bad "adopt should preserve the manifest as *.local.json" \
      "rc=$rc"$'\n'"local.json: $(cat "$t/.config/dotnet-tools.local.json" 2>&1)"$'\n'"$OUT"
fi

# --- adopt refuses when both a hand-authored manifest and its *.local.json already exist ---------
t="$(fresh_target)"; mkdir -p "$t/.config"
printf '%s\n' "$HAND" > "$t/$MANIFEST"
printf '%s\n' '{ "version": 1, "isRoot": true, "tools": {} }' > "$t/.config/dotnet-tools.local.json"
rc=0; run "--adopt" "$t" || rc=$?
if [ "$rc" -ne 0 ] \
   && printf '%s' "$OUT" | grep -qi "REFUSING to adopt $MANIFEST" \
   && diff -q <(printf '%s\n' "$HAND") "$t/$MANIFEST" >/dev/null 2>&1; then
  ok "adopt refuses (and preserves both) when the manifest and its *.local.json both exist"
else
  bad "adopt should fail-closed when *.local.json is already taken" "rc=$rc"$'\n'"$OUT"
fi

# --- regression: the *.props protection this was modelled on still holds -------------------------
# A hand-authored .props (no marker) must still be refused on apply. Guards against a refactor that
# generalised the manifest at the expense of the case it was copied from.
t="$(fresh_target)"; printf '%s\n' '<Project><!-- hand authored --></Project>' > "$t/$PROP"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -ne 0 ] \
   && printf '%s' "$OUT" | grep -qi "REFUSING to overwrite hand-authored $PROP" \
   && grep -q "hand authored" "$t/$PROP"; then
  ok "apply still refuses a hand-authored .props (no marker) — .props path unregressed"
else
  bad ".props refusal must be unchanged" "rc=$rc"$'\n'"$OUT"
fi

echo "sync-build-config fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::sync-build-config fixture FAILED"; exit 1; }
echo "sync-build-config fixture — OK"
