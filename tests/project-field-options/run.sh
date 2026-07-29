#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOL="$ROOT/scripts/project-field-options"
FAKE="$ROOT/tests/project-field-options/fake-gh"
ROSTER="$ROOT/tests/project-field-options/roster.yml"
RESOLVER="$ROOT/tests/project-field-options/resolver.fs"
SCHEMA="$ROOT/docs/coordination/board-schema.md"
SKILL="$ROOT/.claude/skills/cross-repo-coordination"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

ok=0
bad=0
pass() { echo "ok - $1"; ok=$((ok + 1)); }
fail() { echo "not ok - $1: $2"; bad=$((bad + 1)); }
reset_state() { cp "$ROOT/tests/project-field-options/state.initial.json" "$WORK/state.json"; }
run_tool() {
  PROJECT_FIELD_OPTIONS_GH="$FAKE" \
  PROJECT_FIELD_OPTIONS_FAKE_STATE="$WORK/state.json" \
    "$TOOL" "$@"
}

chmod +x "$FAKE" "$TOOL"
reset_state

if run_tool snapshot --output "$WORK/before.json" >/dev/null \
  && [ "$(jq -r .itemTotalCount "$WORK/before.json")" = 3 ] \
  && [ "$(jq -r '[.items[].id] | unique | length' "$WORK/before.json")" = 3 ]; then
  pass "snapshot pages to totalCount with one unique row per item"
else
  fail "complete snapshot" "$(run_tool snapshot --output "$WORK/debug.json" --force 2>&1 || true)"
fi

if run_tool verify-snapshot --snapshot "$WORK/before.json" >/dev/null; then
  pass "snapshot integrity verifies"
else
  fail "snapshot integrity" "verification refused a valid snapshot"
fi

jq '.items[0].repoScope="tampered"' "$WORK/before.json" >"$WORK/tampered.json"
if run_tool verify-snapshot --snapshot "$WORK/tampered.json" >/dev/null 2>&1; then
  fail "tamper refusal" "modified snapshot was accepted"
else
  pass "snapshot tampering is refused"
fi

if run_tool check --snapshot "$WORK/before.json" --roster "$ROSTER" --resolver "$RESOLVER" >/dev/null 2>&1; then
  fail "roster drift" "missing net option passed"
else
  pass "roster-vs-field check reports missing net"
fi

if PROJECT_FIELD_OPTIONS_FAKE_FORBID_MUTATION=1 run_tool add-option --snapshot "$WORK/before.json" --name net >/dev/null 2>&1; then
  fail "apply fence" "add-option ran without --apply"
elif [ "$(jq -r '.fieldMutationCount // 0' "$WORK/state.json")" = 0 ]; then
  pass "add-option refuses mutation without --apply"
else
  fail "apply fence" "field was mutated"
fi

if PROJECT_FIELD_OPTIONS_FAKE_CLEAR_ON_UPDATE=1 run_tool add-option \
    --snapshot "$WORK/before.json" --name net --description "FS.GG.Net" --apply >/dev/null \
  && [ "$(jq -r '[.field.options[].name] | index("net") != null' "$WORK/state.json")" = true ] \
  && [ "$(jq -c '[.items[].repoScope]' "$WORK/state.json")" = '[".github","sdd",null]' ] \
  && [ "$(jq -r '.restoreMutationCount' "$WORK/state.json")" = 1 ]; then
  pass "destructive option recreation restores and verifies every prior assignment"
else
  fail "restore after destructive update" "$(cat "$WORK/state.json")"
fi

if run_tool check --roster "$ROSTER" --resolver "$RESOLVER" >/dev/null; then
  pass "live roster-vs-field check passes after net is added"
else
  fail "post-migration check" "live check failed"
fi

if grep -Fq '| `net` | `P8 Net` |' "$SCHEMA" \
  && grep -FRq 'a `net` item `P8 Net`' "$SKILL" \
  && grep -FRq '`P8 Net`' "$SKILL"; then
  pass "documented Repo Scope net maps to P8 Net"
else
  fail "net phase projection" "board schema and coordination skill must both map net to P8 Net"
fi

reset_state
if PROJECT_FIELD_OPTIONS_FAKE_BAD_TOTAL=1 run_tool snapshot --output "$WORK/partial.json" >/dev/null 2>&1; then
  fail "partial snapshot refusal" "mismatched totalCount was accepted"
else
  pass "partial pagination is refused"
fi

reset_state
run_tool snapshot --output "$WORK/stale.json" --force >/dev/null
jq '.items[0].repoScope="sdd"' "$WORK/state.json" >"$WORK/changed.json"
mv "$WORK/changed.json" "$WORK/state.json"
if PROJECT_FIELD_OPTIONS_FAKE_FORBID_MUTATION=1 run_tool add-option --snapshot "$WORK/stale.json" --name net --apply >/dev/null 2>&1 \
  || [ "$(jq -r '.fieldMutationCount // 0' "$WORK/state.json")" != 0 ]; then
  fail "stale precondition" "mutation ran against changed assignments"
else
  pass "assignment changes after snapshot refuse before mutation"
fi

reset_state
run_tool snapshot --output "$WORK/recovery.json" --force >/dev/null
if PROJECT_FIELD_OPTIONS_FAKE_CLEAR_ON_UPDATE=1 PROJECT_FIELD_OPTIONS_FAKE_DROP_RESTORE=1 \
    run_tool add-option --snapshot "$WORK/recovery.json" --name net --apply >/dev/null 2>&1; then
  fail "partial restore refusal" "verification passed after restore writes were dropped"
else
  pass "partial restore fails closed and leaves the snapshot recoverable"
fi

# --- Class: a closed vocabulary, gated offline (.github#1588) ---------------------------------
# These cases run against the REAL docs/coordination/board-schema.md and temp mutations of it,
# because the drift being gated is between that file and the vocabulary hardcoded in the tool.
# A fixture copy would only ever prove the tool agrees with itself.

if run_tool check --field Class --schema "$SCHEMA" >/dev/null; then
  pass "documented Class options match the closed ItemClass vocabulary"
else
  fail "Class schema check" "$(run_tool check --field Class --schema "$SCHEMA" 2>&1 || true)"
fi

# Drift direction 1: the table lost a vocabulary value.
grep -v '^| `hardening`' "$SCHEMA" >"$WORK/class-short.md"
if run_tool check --field Class --schema "$WORK/class-short.md" >/dev/null 2>&1; then
  fail "Class drift (missing)" "a documented table missing hardening was accepted"
else
  pass "Class check refuses a documented table missing a vocabulary value"
fi

# Drift direction 2: the table grew a value the engine cannot parse. Both directions matter — a
# board option with no `ItemClass` case is an option `reconcile` can never write.
awk '{ print } /^\| `decision` \|/ { print "| `blocker` | not an ItemClass case |" }' \
  "$SCHEMA" >"$WORK/class-extra.md"
if run_tool check --field Class --schema "$WORK/class-extra.md" >/dev/null 2>&1; then
  fail "Class drift (unexpected)" "a documented table with a bogus option was accepted"
else
  pass "Class check refuses a documented table with an option outside the vocabulary"
fi

# The whole point of the tool: finding nothing must never read as finding no drift.
awk '/<!-- class-options:start -->/{skip=1} !skip{print} /<!-- class-options:end -->/{skip=0}' \
  "$SCHEMA" >"$WORK/class-unmarked.md"
if run_tool check --field Class --schema "$WORK/class-unmarked.md" >/dev/null 2>&1; then
  fail "Class marker fence" "an absent marker block passed as a clean result"
else
  pass "Class check refuses an absent marker block rather than passing on nothing"
fi

if run_tool check --field Class --schema "$WORK/absent-schema.md" >/dev/null 2>&1; then
  fail "Class unreadable schema" "a nonexistent schema file passed"
else
  pass "Class check refuses an unreadable schema file"
fi

# An unrecognised --field must not fall through to Repo Scope, which would pass against the real
# roster and print green about a field nothing examined.
if run_tool check --field Severity --schema "$SCHEMA" >/dev/null; then
  pass "documented Severity options match the closed ordered vocabulary"
else
  fail "Severity schema check" "$(run_tool check --field Severity --schema "$SCHEMA" 2>&1 || true)"
fi

# Order is semantic for Severity, unlike Class. Swapping two documented rows must red the gate.
sed '/^| `High` /{h;d}; /^| `Medium` /{G}' "$SCHEMA" >"$WORK/severity-reordered.md"
if run_tool check --field Severity --schema "$WORK/severity-reordered.md" >/dev/null 2>&1; then
  fail "Severity order drift" "a documented table with High/Medium reordered was accepted"
else
  pass "Severity check refuses a reordered vocabulary"
fi

# An unrecognised --field must not fall through to Repo Scope.
if unknown_field_out="$(run_tool check --field Urgency --schema "$SCHEMA" 2>&1)"; then
  fail "unknown field fence" "--field Urgency was gated as Repo Scope: $unknown_field_out"
elif printf '%s' "$unknown_field_out" | grep -Fq "no check is defined for field 'Urgency'"; then
  pass "an unrecognised --field refuses instead of falling through to Repo Scope"
else
  fail "unknown field fence" "$unknown_field_out"
fi

echo "$ok passed; $bad failed"
[ "$bad" -eq 0 ]
