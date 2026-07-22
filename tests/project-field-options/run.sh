#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOL="$ROOT/scripts/project-field-options"
FAKE="$ROOT/tests/project-field-options/fake-gh"
ROSTER="$ROOT/tests/project-field-options/roster.yml"
RESOLVER="$ROOT/tests/project-field-options/resolver.fs"
SCHEMA="$ROOT/docs/coordination/board-schema.md"
SKILL="$ROOT/.claude/skills/cross-repo-coordination/SKILL.md"
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
  && grep -Fq 'a `net` item `P8 Net`' "$SKILL" \
  && grep -Fq '`P8 Net`' "$SKILL"; then
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

echo "$ok passed; $bad failed"
[ "$bad" -eq 0 ]
