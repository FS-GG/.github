#!/usr/bin/env bash
# Fixture for `parse` in scripts/NewSddWorkspace/Program.fs — the new-sdd-workspace CLI arg parser.
#
# The regression under guard is .github#388: `parse` is a pure `string list -> Result<Options,string>`,
# yet nothing exercised it, so a `--profile`/`--ref` flag-as-value bug and an unvalidated `--profile`
# shipped and were only caught late. This fixture pins the grammar so the next edit cannot silently
# reintroduce a fail-open parse — the same "a gate reports green on a missing subject" shape the org
# keeps finding late.
#
# It drives the REAL compiled CLI end-to-end (never retyping the parse logic) and reads the parse
# result off the process's exit-code contract, which `main` defines exactly:
#   exit 2   → parse REJECTED — main prints `error: <msg>` + usage        [Error leg]
#   exit 127 → parse ACCEPTED → run → preflight "fsgg-sdd is not on PATH"  [Ok leg]
# To make the Ok leg hermetic (no network, no scaffold, no side effects) the CLI is run with
# `fsgg-sdd` scrubbed from PATH, so a valid parse stops at that preflight instead of scaffolding for
# real. A meta-assertion at the end confirms the harness stayed hermetic (the target was never created).
#
# Deliberately asserts ONLY the committed grammar: it does not touch `--pinned` (an in-flight flag on
# an unmerged branch) — pinning a message for a flag mid-flight would red the moment that lands.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
PROJ="$REPO_ROOT/scripts/NewSddWorkspace/NewSddWorkspace.fsproj"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -f "$PROJ" ] || { echo "::error::missing $PROJ — nothing to test"; exit 1; }

echo "new-sdd-workspace parse fixture — building the CLI (Release)…"
dotnet build "$PROJ" -c Release --nologo -v quiet 1>&2

# Newest match wins, so a leftover older-TFM output dir can't feed a stale dll to the run below.
DLL="$(find "$REPO_ROOT/scripts/NewSddWorkspace/bin/Release" -name new-sdd-workspace.dll 2>/dev/null | sort | tail -1)"
[ -n "$DLL" ] && [ -f "$DLL" ] || { echo "::error::could not locate built new-sdd-workspace.dll under bin/Release"; exit 1; }
echo "new-sdd-workspace parse fixture — dll='$DLL'"

# Scrub `fsgg-sdd` from the child's PATH by handing it only the directory the dotnet muxer lives in
# (onPath does a non-recursive PATH scan, and fsgg-sdd is a global tool in a *different* dir). On a
# fresh CI runner fsgg-sdd is absent anyway; this keeps the fixture hermetic on a dev box that has it.
DOTNET_DIR="$(dirname "$(command -v dotnet)")"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/new-sdd-workspace-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
# A target dir that every VALID parse below must leave UNCREATED — proof the Ok leg stopped at the
# fsgg-sdd preflight and never reached scaffolding (step 1 is the first thing to create it).
TGT="$WORK/never-created"

# cli <args…> → rc; combined output in $OUT. stdin from /dev/null so the no-arg case stays on the
# non-interactive parse path (main only prompts on an interactive terminal), not the wizard.
cli() { local rc=0; OUT="$(PATH="$DOTNET_DIR" dotnet "$DLL" "$@" </dev/null 2>&1)" || rc=$?; return "$rc"; }

# expect_err <desc> <needle> -- <cli args…>   (parse must be rejected: exit 2 + message substring)
expect_err() {
  local desc="$1" needle="$2"; shift 3   # drop desc, needle, and the literal '--' separator
  local rc=0; cli "$@" || rc=$?
  if [ "$rc" -eq 2 ] && printf '%s' "$OUT" | grep -qF -- "$needle"; then
    ok "$desc"
  else
    bad "$desc" "want rc=2 + substring '$needle'; got rc=$rc"$'\n'"--- output ---"$'\n'"$OUT"
  fi
}

# expect_ok <desc> -- <cli args…>   (parse must be accepted: reaches preflight, exit 127, no scaffold)
expect_ok() {
  local desc="$1"; shift 2               # drop desc and the literal '--' separator
  local rc=0; cli "$@" || rc=$?
  if [ "$rc" -eq 127 ] && printf '%s' "$OUT" | grep -qF -- "fsgg-sdd is not on PATH"; then
    ok "$desc"
  else
    bad "$desc" "want rc=127 + preflight panel (parse accepted); got rc=$rc"$'\n'"--- output ---"$'\n'"$OUT"
  fi
}

# ── Error leg: the parse grammar rejects, exit 2 with the actionable message ──────────────────────

expect_err "no args → positionals required (non-interactive falls through to parse, not the wizard)" \
  "target dir and product name are required" --
expect_err "a single positional is not enough" \
  "target dir and product name are required" -- "$TGT"
expect_err "a flag before the two positionals is rejected (target/product must come first)" \
  "target dir and product name are required" -- --profile game
# The #388 flag-as-value bug: a following --flag is a MISSING value, not the value.
expect_err "--profile swallowing the next flag as its value is caught (#388)" \
  "--profile needs a value (got flag '--ref')" -- "$TGT" P --profile --ref v1
expect_err "--ref swallowing the next flag as its value is caught (#388)" \
  "--ref needs a value (got flag '--upgrade')" -- "$TGT" P --ref --upgrade
# The #388 unvalidated-profile bug: an unknown profile is refused on the CLI path, not late in scaffold.
expect_err "an unknown --profile value is rejected against the known set (#388)" \
  "unknown profile 'bogus'" -- "$TGT" P --profile bogus
expect_err "--profile with no following token needs a value" \
  "--profile needs a value" -- "$TGT" P --profile
expect_err "--ref with no following token needs a value" \
  "--ref needs a value" -- "$TGT" P --ref
expect_err "an unknown flag is named, not silently ignored" \
  "unknown argument: --bogus" -- "$TGT" P --bogus
# Coordination flags carry the same flag-as-value guard as --profile/--ref (#388).
expect_err "--board swallowing the next flag as its value is caught" \
  "--board needs a value (got flag '--upgrade')" -- "$TGT" P --board --upgrade
expect_err "--board with no following token needs a value" \
  "--board needs a value" -- "$TGT" P --board
expect_err "--chore-locks swallowing the next flag as its value is caught" \
  "--chore-locks needs a value (got flag '--no-governance')" -- "$TGT" P --chore-locks --no-governance
expect_err "--chore-locks with no following token needs a value" \
  "--chore-locks needs a value" -- "$TGT" P --chore-locks

# ── Ok leg: the parse grammar accepts, reaching the fsgg-sdd preflight (exit 127, no scaffold) ─────

expect_ok "the bare two-positional form parses (Profile defaults to the provider default)" \
  -- "$TGT" P
# Each id in `profiles` (Program.fs) must parse. Kept in lockstep with that list by hand; a removed
# profile red-flags here, and the unknown-profile assertion above pins the closed set.
for prof in game app headless-scene governed sample-pack; do
  expect_ok "--profile $prof parses" -- "$TGT" P --profile "$prof"
done
expect_ok "--upgrade toggles" -- "$TGT" P --upgrade
expect_ok "--no-governance toggles" -- "$TGT" P --no-governance
expect_ok "--no-coordination toggles" -- "$TGT" P --no-coordination
expect_ok "--board takes an owner/title value" -- "$TGT" P --board acme/Roadmap
expect_ok "--board accepts an owner-only value (defaults the title)" -- "$TGT" P --board acme
expect_ok "--chore-locks takes a value" -- "$TGT" P --board acme/Roadmap --chore-locks "acme/Product.X#5,acme/Product.Y#7"
expect_ok "flags combine, and --ref takes a value" -- "$TGT" P --upgrade --no-governance --ref v1.2.3

# ── main's non-parse dispatch: -h / --help print usage and exit 0 ─────────────────────────────────

for h in --help -h; do
  rc=0; cli "$h" || rc=$?
  if [ "$rc" -eq 0 ] && printf '%s' "$OUT" | grep -qF "new-sdd-workspace"; then
    ok "$h prints usage and exits 0"
  else
    bad "$h should print usage and exit 0" "got rc=$rc"$'\n'"$OUT"
  fi
done

# ── Meta: the hermetic harness held — no valid parse leaked a real scaffold ───────────────────────
if [ ! -e "$TGT" ]; then
  ok "hermetic: no accepted parse created the target dir (all stopped at preflight)"
else
  bad "a valid parse scaffolded for real — the PATH scrub failed to hide fsgg-sdd" "found: $TGT"
fi

echo "new-sdd-workspace parse fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::new-sdd-workspace parse fixture FAILED"; exit 1; }
echo "new-sdd-workspace parse fixture — OK"
