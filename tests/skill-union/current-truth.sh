#!/usr/bin/env bash
# Focused regression fixture for the M0 skill current-truth repair (.github#1410).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
CHECK="$HERE/check-current-truth.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-current-truth.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
failcount=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; failcount=$((failcount + 1)); }

if python3 "$CHECK" --root "$ROOT"; then
  ok "the real three-root skills carry one coherent current truth"
else
  bad "the real two-root skill audit failed"
fi

seed() {
  local target="$1"
  for runtime in .agents/skills .claude/skills; do
    mkdir -p "$target/$runtime"
    for skill in check-board cross-repo-coordination cut-nuget-release drive-board \
      intra-repo-parallel-work lane-steward p-add padd-item pnext-item publishing-and-deployment \
      spectre-console work-board work-roadmap; do
      cp -R "$ROOT/$runtime/$skill" "$target/$runtime/$skill"
    done
  done
  mkdir -p "$target/docs" "$target/.github/workflows"
  # The skills link to ADRs and roadmaps that change as the publication route changes (#4966 added a
  # roadmap, #4943 an ADR), and a hand-listed copy rotted twice; the link check needs the whole targets.
  cp -R "$ROOT/docs/adr" "$ROOT/docs/roadmaps" "$target/docs/"
  cp "$ROOT/.github/workflows/dispatch-sender.yml" "$target/.github/workflows/"
  cp "$ROOT/default.json" "$target/"
}

expect_finding() {
  local name="$1" needle="$2" target="$3" out rc=0
  out="$(python3 "$CHECK" --root "$target" 2>&1)" || rc=$?
  if [ "$rc" -eq 1 ] && grep -qF "$needle" <<<"$out"; then
    ok "$name"
  else
    bad "$name (wanted exit 1 + '$needle', got exit $rc: $out)"
  fi
}

LANE="$WORK/lane"
seed "$LANE"
for runtime in .agents/skills .claude/skills; do
  printf '\nApply it with `scripts/fsgg-coord set-paths <issue> --paths "<the above>"`.\n' >> "$LANE/$runtime/lane-steward/SKILL.md"
done
expect_finding \
  "a resurrected V1 set-paths narrowing recipe is refused" \
  "obsolete V1 scan/lanes/set-paths/widen/take/claim recipe returned" \
  "$LANE"

LANE_WIDEN="$WORK/lane-widen"
seed "$LANE_WIDEN"
for runtime in .agents/skills .claude/skills; do
  printf '\nExpand with `scripts/fsgg-coord widen <issue> --paths "tests/new-surface/"`.\n' >> "$LANE_WIDEN/$runtime/lane-steward/SKILL.md"
done
expect_finding \
  "a resurrected V1 widen recipe is refused" \
  "obsolete V1 scan/lanes/set-paths/widen/take/claim recipe returned" \
  "$LANE_WIDEN"

LANE_CLAIM="$WORK/lane-claim"
seed "$LANE_CLAIM"
for runtime in .agents/skills .claude/skills; do
  printf '\nTake it with `scripts/fsgg-coord claim <issue>` and `scripts/fsgg-coord lanes`.\n' >> "$LANE_CLAIM/$runtime/lane-steward/SKILL.md"
done
expect_finding \
  "a resurrected V1 claim or lanes recipe is refused" \
  "obsolete V1 scan/lanes/set-paths/widen/take/claim recipe returned" \
  "$LANE_CLAIM"

LANE_REFLOW="$WORK/lane-reflow"
seed "$LANE_REFLOW"
for runtime in .agents/skills .claude/skills; do
  python3 - "$LANE_REFLOW/$runtime/lane-steward/SKILL.md" <<'PY'
import re, sys
path = sys.argv[1]
text = open(path).read()
# Reflow the two wrapped statements onto different line breaks; the meaning is unchanged.
text = text.replace("set-paths,\nwiden,", "set-paths, widen,").replace("does not select,\nclaim,", "does not select, claim,")
open(path, "w").write(text)
PY
done
out="$(python3 "$CHECK" --root "$LANE_REFLOW" 2>&1)" && ok "a reflowed lane-steward statement is not a finding" || bad "a reflowed lane-steward statement is not a finding ($out)"

LANE_AUTH="$WORK/lane-auth"
seed "$LANE_AUTH"
for runtime in .agents/skills .claude/skills; do
  sed -i 's/route is authorized for organization V2/route is available for organization V2/' "$LANE_AUTH/$runtime/lane-steward/SKILL.md"
done
expect_finding \
  "lane-steward cannot drop the statement that V1 routes are unauthorized" \
  "no V1 narrowing/claim route is authorized" \
  "$LANE_AUTH"

BOARD_LOOP="$WORK/board-loop"
seed "$BOARD_LOOP"
for runtime in .agents/skills .claude/skills; do
  printf '\nSpawn with isolation: "worktree" through the Agent tool.\n' >> "$BOARD_LOOP/$runtime/work-board/references/host-loop.md"
done
expect_finding \
  "host-specific orchestration syntax in work-board is refused" \
  "host-specific orchestration syntax returned" \
  "$BOARD_LOOP"

ROADMAP_LOOP="$WORK/roadmap-loop"
seed "$ROADMAP_LOOP"
for runtime in .agents/skills .claude/skills; do
  sed -i 's/FSGG_WORKER/WORKER_ID/g' "$ROADMAP_LOOP/$runtime/work-roadmap/references/host-loop.md"
done
expect_finding \
  "work-roadmap host loop keeps its portability rules" \
  "host loop missing portability rule" \
  "$ROADMAP_LOOP"

IMPLICIT="$WORK/implicit"
seed "$IMPLICIT"
for runtime in .agents/skills .claude/skills; do
  sed -i 's/allow_implicit_invocation: true/allow_implicit_invocation: false/' "$IMPLICIT/$runtime/work-board/agents/openai.yaml"
done
expect_finding \
  "canonical work-board cannot silently become explicit-only" \
  "work-board: invocation policy must be implicit/default" \
  "$IMPLICIT"

PUBLISH="$WORK/publish"
seed "$PUBLISH"
for runtime in .agents/skills .claude/skills; do
  sed -i 's/## Historical rollout record/## Public nuget.org (decided, wiring pending — ADR-0012 + ADR-0013)/' \
    "$PUBLISH/$runtime/publishing-and-deployment/SKILL.md"
done
expect_finding \
  "stable dual-publish guidance rejects a resurrected pending-rollout claim" \
  "stale live claim returned" \
  "$PUBLISH"

echo "--------------------------------------------"
echo "skill current-truth fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
