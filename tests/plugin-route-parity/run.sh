#!/usr/bin/env bash
# Selftest for scripts/check-plugin-route-parity.py (.github#2230 AC3).
#
# Every leg builds a SYNTHETIC tree from scratch and points the gate at it with --root. The tree is
# never the real repository: a fixture that mutates the working tree proves only that the gate reads
# the working tree, and it goes green for free the day someone deletes the subject. Building the
# subject here means the anchor is produced by this file, which is the property tests/gate-mutation's
# "ADDING A LEG" note requires of a gate's own fixture.
#
# The point of the negative legs is the one thing a green gate cannot tell you: that it can still say
# NO. Each asserts BOTH the exit code and the REASON, because a gate that fails for the wrong reason
# is a gate that will pass for the wrong reason later.
#
# Exit-code contract under test (scripts/lib/gate.py): 0 OK, 1 FINDING, 3 NO-VERDICT-PERMANENT.
# Note especially the exit-3 legs: "I could not look" must never share a code with "I looked and it is
# clean" (#266, #320).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-plugin-route-parity.py"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

pass=0
failcount=0

# Build a complete, COHERENT synthetic tree at $1. Legs mutate one thing about it and expect red.
mk() {
  local t="$1"
  rm -rf "$t"
  mkdir -p "$t/.claude-plugin" \
           "$t/plugins/fsgg-routes/.claude-plugin" \
           "$t/plugins/fsgg-routes/agents" \
           "$t/.claude"

  local variant model
  for variant in drive-board-best work-board-best drive-board-normal work-board-normal; do
    case "$variant" in
      *-best)   model="opus" ;;
      *)        model="sonnet" ;;
    esac
    mkdir -p "$t/.claude/skills/$variant"
    {
      printf '%s\n\n' "# $variant"
      printf '%s\n\n' "Pass this route explicitly to every subagent spawn:"
      printf '%s\n' "| runtime | model | effort |"
      printf '%s\n' "|---|---|---|"
      printf '%s\n' "| Codex | \`gpt-5.6\` | \`medium\` |"
      printf '%s\n\n' "| Claude Code | \`$model\` | \`high\` |"
      printf '%s\n\n' "**Repair-phase route.** Escalate to the best tier."
      # A -normal variant restates the escalated table; a -best variant says "same route" and has none.
      if [ "$model" = "sonnet" ]; then
        printf '%s\n' "| runtime | model | effort |"
        printf '%s\n' "|---|---|---|"
        printf '%s\n' "| Codex | \`gpt-5.6-sol\` | \`medium\` |"
        printf '%s\n' "| Claude Code | \`opus\` | \`high\` |"
      fi
    } > "$t/.claude/skills/$variant/SKILL.md"
  done

  local agent
  for agent in fsgg-worker-best fsgg-critic-best fsgg-worker-normal fsgg-critic-normal; do
    case "$agent" in
      *-best)   model="opus" ;;
      *)        model="sonnet" ;;
    esac
    {
      printf '%s\n' "---"
      printf '%s\n' "name: $agent"
      printf '%s\n' "description: synthetic fixture definition"
      printf '%s\n' "model: $model"
      printf '%s\n' "effort: high"
      printf '%s\n\n' "---"
      printf '%s\n' "Body."
    } > "$t/plugins/fsgg-routes/agents/$agent.md"
  done

  cat > "$t/.claude-plugin/marketplace.json" <<'JSON'
{
  "name": "fsgg",
  "description": "fixture",
  "owner": { "name": "FS-GG" },
  "plugins": [
    { "name": "fsgg-routes", "description": "fixture", "source": "./plugins/fsgg-routes" }
  ]
}
JSON

  cat > "$t/plugins/fsgg-routes/.claude-plugin/plugin.json" <<'JSON'
{ "name": "fsgg-routes", "description": "fixture", "version": "0.1.0" }
JSON

  cat > "$t/.claude/settings.json" <<'JSON'
{
  "extraKnownMarketplaces": {
    "fsgg": { "source": { "source": "github", "repo": "FS-GG/.github" } }
  },
  "enabledPlugins": { "fsgg-routes@fsgg": true }
}
JSON
}

# assert <expected-exit> <expected-substring> <description>   (tree already built/mutated at $WORK/t)
assert() {
  local want_rc="$1" want_msg="$2" desc="$3"
  local rc=0 out
  out="$(python3 "$GATE" --root "$WORK/t" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want_rc" ]; then
    printf 'FAIL: %s\n  expected exit %s, got %s\n  output: %s\n' "$desc" "$want_rc" "$rc" "$out"
    failcount=$((failcount + 1))
    return
  fi
  if ! printf '%s' "$out" | grep -qF -- "$want_msg"; then
    printf 'FAIL: %s\n  exit %s was right but the reason was not.\n  wanted substring: %s\n  output: %s\n' \
      "$desc" "$rc" "$want_msg" "$out"
    failcount=$((failcount + 1))
    return
  fi
  printf 'ok: %s\n' "$desc"
  pass=$((pass + 1))
}

# --- POSITIVE CONTROL -----------------------------------------------------------------------------
mk "$WORK/t"
assert 0 "check-plugin-route-parity: OK" "a coherent tree passes"

# --- ASSERTION 1: ordinary route parity -----------------------------------------------------------
mk "$WORK/t"
sed -i 's/^model: sonnet$/model: opus/' "$WORK/t/plugins/fsgg-routes/agents/fsgg-worker-normal.md"
assert 1 "ORDINARY ROUTE DRIFT" "a worker definition that outruns its variant's table is caught"

mk "$WORK/t"
sed -i 's/^effort: high$/effort: low/' "$WORK/t/plugins/fsgg-routes/agents/fsgg-critic-normal.md"
assert 1 "ORDINARY ROUTE DRIFT" "a CRITIC definition drifting is caught, not only a worker"

mk "$WORK/t"
sed -i 's/| Claude Code | `sonnet` | `high` |/| Claude Code | `haiku` | `high` |/' \
  "$WORK/t/.claude/skills/drive-board-normal/SKILL.md"
assert 1 "ORDINARY ROUTE DRIFT" "drift authored on the TABLE side is caught too"

# --- ASSERTION 2: repair-phase parity (the supersession claim, made mechanical) --------------------
mk "$WORK/t"
sed -i 's/^model: opus$/model: sonnet/' "$WORK/t/plugins/fsgg-routes/agents/fsgg-worker-best.md"
assert 1 "REPAIR-PHASE ROUTE DRIFT" "downgrading the best worker breaks every variant's repair route"

mk "$WORK/t"
sed -i 's/| Claude Code | `opus` | `high` |/| Claude Code | `sonnet` | `high` |/' \
  "$WORK/t/.claude/skills/drive-board-normal/SKILL.md"
assert 1 "REPAIR-PHASE ROUTE DRIFT" "a -normal repair table that stops escalating is caught"

# --- ASSERTION 3: one home ------------------------------------------------------------------------
mk "$WORK/t"
mkdir -p "$WORK/t/.claude/agents"
cp "$WORK/t/plugins/fsgg-routes/agents/fsgg-worker-best.md" "$WORK/t/.claude/agents/fsgg-worker-best.md"
assert 1 "TWO HOMES" "a definition that is ALSO a loose file is caught even when the copies agree"

mk "$WORK/t"
mkdir -p "$WORK/t/.claude/agents"
printf -- '---\nname: fsgg-worker-repair\nmodel: opus\neffort: high\n---\n' \
  > "$WORK/t/.claude/agents/fsgg-worker-repair.md"
assert 1 "UNROUTED DEFINITION" "reintroducing the superseded fsgg-worker-repair tier is caught"

# --- ASSERTION 4: the #2203 binding constraint, both directions -----------------------------------
mk "$WORK/t"
cat > "$WORK/t/.claude/settings.json" <<'JSON'
{
  "extraKnownMarketplaces": {
    "fsgg": { "source": { "source": "directory", "path": "/home/dev/projects/.github" } }
  },
  "enabledPlugins": { "fsgg-routes@fsgg": true }
}
JSON
assert 1 "not 'github'" "a directory MARKETPLACE source — the hazard #2203 forbids — is caught"

mk "$WORK/t"
sed -i 's#"source": "./plugins/fsgg-routes"#"source": { "source": "github", "repo": "FS-GG/.github" }#' \
  "$WORK/t/.claude-plugin/marketplace.json"
assert 1 "must stay a relative" "rewriting the relative PLUGIN source is caught"

mk "$WORK/t"
sed -i 's#"repo": "FS-GG/.github"#"repo": "FS-GG/Coordination"#' "$WORK/t/.claude/settings.json"
assert 1 "not 'FS-GG/.github'" "a marketplace pointed at the wrong repository is caught"

mk "$WORK/t"
sed -i 's#"source": "./plugins/fsgg-routes"#"source": "./plugins/nope"#' \
  "$WORK/t/.claude-plugin/marketplace.json"
assert 1 "does not resolve to a plugin manifest" "a relative source pointing nowhere is caught"

# --- ASSERTION 5: wiring --------------------------------------------------------------------------
mk "$WORK/t"
sed -i 's/"enabledPlugins": { "fsgg-routes@fsgg": true }/"enabledPlugins": {}/' \
  "$WORK/t/.claude/settings.json"
assert 1 "does not enable" "a known-but-unenabled plugin contributes no agents, and is caught"

mk "$WORK/t"
rm "$WORK/t/plugins/fsgg-routes/agents/fsgg-critic-best.md"
assert 1 "MISSING DEFINITION" "a route table naming a definition that does not exist is caught"

mk "$WORK/t"
sed -i 's/^name: fsgg-worker-best$/name: fsgg-worker-bset/' \
  "$WORK/t/plugins/fsgg-routes/agents/fsgg-worker-best.md"
assert 1 "the runtime dispatches by the frontmatter name" "a frontmatter/filename mismatch is caught"

# --- FAIL-CLOSED: could-not-look must never be green (#266) ---------------------------------------
mk "$WORK/t"
rm -rf "$WORK/t/.claude/skills/work-board-best"
assert 3 "no verdict" "a missing board variant is a NO-VERDICT, not a pass"

mk "$WORK/t"
sed -i '/| Claude Code |/d' "$WORK/t/.claude/skills/drive-board-best/SKILL.md"
assert 3 "no verdict" "a route table with no Claude Code row is a NO-VERDICT, not a vacuous pass"

mk "$WORK/t"
sed -i '/\*\*Repair-phase route\.\*\*/d' "$WORK/t/.claude/skills/drive-board-best/SKILL.md"
assert 3 "no verdict" "a variant with no repair-phase section is a NO-VERDICT"

mk "$WORK/t"
printf 'no frontmatter here\n' > "$WORK/t/plugins/fsgg-routes/agents/fsgg-worker-best.md"
assert 3 "no verdict" "a definition the runtime could not read is a NO-VERDICT"

mk "$WORK/t"
printf '{ not json\n' > "$WORK/t/.claude-plugin/marketplace.json"
assert 3 "no verdict" "an unparsable catalog is a NO-VERDICT"

mk "$WORK/t"
rm "$WORK/t/.claude/settings.json"
assert 3 "no verdict" "unreadable settings are a NO-VERDICT, not an assumed-clean tree"

printf '\n%s passed, %s failed\n' "$pass" "$failcount"
[ "$failcount" -eq 0 ]
