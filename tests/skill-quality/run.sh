#!/usr/bin/env bash
# Behavioral fixture for the unified semantic skill-quality gate (.github#1415).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-quality-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# Prove the checked-in catalog passes through the exact local/CI entry point first.
"$ROOT/scripts/check-skill-quality"
python3 "$ROOT/tests/skill-quality/driver-feedback-delivery.py"
python3 "$ROOT/tests/skill-quality/review-round-contract.py"
python3 "$ROOT/tests/skill-quality/roadmap-critique-contract.py"
python3 "$ROOT/tests/skill-quality/agent-definition-coverage.py"
python3 "$ROOT/tests/skill-quality/recency-comment-edit.py"

# .github#2666 — a gate nothing invokes is graded NOT_MEASURED at best. Each gate above asserts its
# own workflow path filter, but the workflow only ever runs THIS file, so a gate dropped from the
# list above stops running while every path filter still reads correctly. Enumerating the directory
# closes that for the whole set at once: adding a gate script without calling it reds here.
for gate in "$ROOT"/tests/skill-quality/*.py; do
  if ! grep -Fq "tests/skill-quality/$(basename "$gate")" "$ROOT/tests/skill-quality/run.sh"; then
    echo "skill-quality fixture: $(basename "$gate") exists but run.sh never invokes it — CI runs" \
      "only run.sh, so that gate is dead code wearing a green check" >&2
    exit 1
  fi
done

ENGINE="$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
"$ENGINE" command-contract --json >"$WORK/contract.json"

pass=0
fail=0

seed() {
  rm -rf "$WORK/tree"
  mkdir -p "$WORK/tree/scripts" "$WORK/tree/tests"
  cp -a "$ROOT/.claude" "$ROOT/.agents" "$WORK/tree/"
  cp -a "$ROOT/.github" "$ROOT/docs" "$ROOT/profile" "$WORK/tree/"
  cp -a "$ROOT/registry" "$WORK/tree/"
  cp "$ROOT/default.json" "$WORK/tree/"
  cp -a "$ROOT/tests/skill-registry" "$ROOT/tests/skill-quality" "$WORK/tree/tests/"
  cp "$ROOT/scripts/generate-driver-manifest" "$WORK/tree/scripts/"
  cp "$ROOT/scripts/generate-projections" "$WORK/tree/scripts/"
  mkdir -p "$WORK/tree/src/FS.GG.Coord.Cli/bin/Release"
  cp -a "$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0" \
    "$WORK/tree/src/FS.GG.Coord.Cli/bin/Release/"
}

expect_rejection() {
  local label="$1" evidence="$2" contract="${3:-$WORK/contract.json}"
  local rc=0
  python3 "$ROOT/scripts/check-skill-quality.py" \
    --root "$WORK/tree" --contract "$contract" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted exit 1 containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

# .github#2510 — `seed` predates this gate and does not copy `.agent-skill-roots`, which the coverage
# gate reads to discover the skill roots it scans for named agent types. Wrapping rather than widening
# `seed` itself keeps the 30-odd pre-existing rejection cases on the exact tree they were written
# against; the gate is deliberately NOT relaxed to guess a default root list, because a tree that
# cannot say where its skills live is a tree this gate cannot honestly clear.
seed_agents() {
  seed
  cp "$ROOT/.agent-skill-roots" "$WORK/tree/"
}

# The routed-dispatch coverage gate has its own rejection helper because it is a separate entry point
# with its own `--root`. Same three-part shape as `expect_rejection`: exit 1 and the exact finding
# text, so a regression that merely changes the message reds here too.
expect_agent_rejection() {
  local label="$1" evidence="$2"
  local rc=0
  python3 "$ROOT/tests/skill-quality/agent-definition-coverage.py" \
    --root "$WORK/tree" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted exit 1 containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

# .github#2666. Same three-part shape again — its own `--root` entry point, exit 1, and the exact
# finding text — so a regression that merely reworded the message reds here too.
expect_recency_rejection() {
  local label="$1" evidence="$2"
  local rc=0
  python3 "$ROOT/tests/skill-quality/recency-comment-edit.py" \
    --root "$WORK/tree" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted exit 1 containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

mutate_workboard_phrase() {
  python3 - "$WORK/tree" "$1" "$2" <<'PY'
import sys
from pathlib import Path

root, old, new = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/work-board/references/backlog-triage.md"
    text = path.read_text()
    if old not in text:
        raise SystemExit(f"fixture phrase missing: {old}")
    path.write_text(text.replace(old, new))
PY
}

seed
printf '\n```sh\nscripts/fsgg-coord widen .github#1 --apply\n```\n' \
  >>"$WORK/tree/docs/coordination/semantic-regression.md"
expect_rejection "documented option must belong to its verb" "--apply is not a flag of widen"

seed
printf '\n[missing reference](references/does-not-exist.md)\n' \
  >>"$WORK/tree/.claude/skills/check-board/SKILL.md"
expect_rejection "broken relative links fail before distribution" "broken relative link"

# .github#2343: a link inside a kit-delivered skill (registry/repos.yml `kit:` block) that resolves
# to a real file OUTSIDE every kit skill directory (here, docs/coordination/README.md) is exactly
# the shape that shipped a dead link into every coordination-kit receiver, because a receiver never
# materializes docs/ — only the kit skill directories themselves. `validate_links` alone cannot see
# this: the target genuinely EXISTS in this checkout, so the older, weaker check stays green. Only
# `validate_receiver_safe_links` — added by this item — proves the receiver-true fact.
seed
printf '\n[escapes the kit](../../../docs/coordination/README.md)\n' \
  >>"$WORK/tree/.claude/skills/check-board/SKILL.md"
expect_rejection "a kit skill link resolving outside every kit-delivered directory is rejected" \
  "receiver-unsafe relative link"

seed
python3 - "$WORK/tree/tests/skill-quality/forward-triggers.json" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
doc = json.loads(path.read_text())
doc["cases"] = doc["cases"][:-1]
path.write_text(json.dumps(doc))
PY
expect_rejection "a missing forward trigger class is rejected" "forward trigger classes differ"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/drive-board/references/backlog-triage.md"
    path.write_text(path.read_text().replace("An empty Ready batch is not completion", "A dry wave may stop"))
PY
expect_rejection "drive-board cannot terminate over actionable backlog" \
  "backlog planning contract lost 'An empty Ready batch is not completion'"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/p-add/SKILL.md"
    path.write_text(path.read_text().replace("reuse an existing matching issue", "always create a new issue"))
PY
expect_rejection "p-add cannot duplicate an existing matching issue" \
  "p-add: filing contract lost 'reuse an existing matching issue'"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/padd-item/SKILL.md"
    path.write_text(path.read_text().replace("Never silently fall back", "Silently fall back"))
PY
expect_rejection "padd-item cannot fall back from the workspace's configured board" \
  "padd-item: workspace filing contract lost 'Never silently fall back'"

seed
mutate_workboard_phrase "without mutating the board" "after attempting a board write"
expect_rejection "work-board missing wiring fails without mutation" \
  "work-board: backlog planning contract lost 'without mutating the board'"

seed
mutate_workboard_phrase "Promotion changes eligibility, not assignment" "Promotion assigns the chosen item"
expect_rejection "work-board promotion remains scheduler-selected and collision-safe" \
  "work-board: backlog planning contract lost 'Promotion changes eligibility, not assignment'"

seed
mutate_workboard_phrase "follow-up filed by the preceding wave" "follow-up already present at startup"
expect_rejection "work-board sees worker-filed backlog on the next wave" \
  "work-board: backlog planning contract lost 'follow-up filed by the preceding wave'"

seed
mutate_workboard_phrase "An empty Ready batch is not completion" "An empty Ready batch is completion"
expect_rejection "work-board cannot terminate over actionable backlog" \
  "work-board: backlog planning contract lost 'An empty Ready batch is not completion'"

seed
mutate_workboard_phrase "Do not spin" "Poll the same rows until they change"
expect_rejection "work-board reports parked and human backlog without spinning" \
  "work-board: backlog planning contract lost 'Do not spin'"

# .github#2088: two concurrent waves, three implementer slots each, two RESERVED review slots (never
# filled by implementers), a testable three-or-fewer consolidation threshold, and an explicit
# fleet-wide EX_RATE stop. These legs are the point, not the passing case: a driver that let a wave
# fill all eight slots with implementers, or scoped EX_RATE to one wave, or let the two host-loop
# copies drift, or let a routed variant restate the numbers instead of inheriting them, must be
# rejected — each leg below breaks exactly one of those and checks the gate catches it.
mutate_hostloop_phrase() {
  python3 - "$WORK/tree" "$1" "$2" "$3" <<'PY'
import sys
from pathlib import Path

root, driver, old, new = Path(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills" / driver / "references/host-loop.md"
    text = path.read_text()
    if old not in text:
        raise SystemExit(f"fixture phrase missing: {old}")
    path.write_text(text.replace(old, new))
PY
}

seed
mutate_hostloop_phrase drive-board "consolidation-threshold=3" "consolidation-threshold=2"
expect_rejection "drive-board cannot vary the generated consolidation threshold" \
  "generated process contract is stale"

seed
mutate_hostloop_phrase work-board "RESERVED, not advisory" "reserved when convenient"
expect_rejection "work-board's review slots stay reserved rather than fillable by implementers" \
  "work-board: host-loop lost two-wave contract statement 'RESERVED, not advisory'"

seed
mutate_hostloop_phrase work-board "fleet-wide stop for BOTH waves" "a stop for the reporting wave only"
expect_rejection "work-board cannot scope an EX_RATE stop to a single wave" \
  "work-board: host-loop lost two-wave contract statement 'fleet-wide stop for BOTH waves'"

seed
mutate_hostloop_phrase work-board "consolidation-threshold=3" "consolidation-threshold=2"
expect_rejection "the two host-loop copies cannot state the wave model differently" \
  "two-wave contract: drive-board and work-board host-loop copies state the wave model differently"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/drive-board-best/SKILL.md"
    text = path.read_text()
    path.write_text(
        text.replace(
            "Never let a host default",
            "Run three implementer slots per wave. Never let a host default",
        )
    )
PY
expect_rejection "a routed variant cannot restate the two-wave contract instead of inheriting it" \
  "drive-board-best: restates the two-wave contract"

# .github#1574: a schema id the semantic gate does not support must be an ERROR from the semantic
# gate itself. Its sibling validate_invocations already shouts, and that shout is repairable by
# editing one literal — which is exactly how the polarity assertions got switched off unnoticed.
seed
python3 - "$WORK/contract.json" "$WORK/contract-unsupported-schema.json" <<'PY'
import json
import sys
from pathlib import Path

source, target = Path(sys.argv[1]), Path(sys.argv[2])
doc = json.loads(source.read_text())
doc["schema"] = "fsgg.coord.commands/2"
target.write_text(json.dumps(doc))
PY
expect_rejection "an unsupported contract schema cannot silently disarm the polarity gate" \
  "semantic polarity gate cannot run" "$WORK/contract-unsupported-schema.json"

seed
sed -i '/GENERATED: fsgg-versions/{n;s/^/STALE /;}' "$WORK/tree/docs/architecture.md"
rc=0
bash "$WORK/tree/scripts/generate-projections" --check >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] && grep -Fq -- "STALE" "$WORK/out"; then
  echo "PASS  stale generated board/version truth is rejected"
  pass=$((pass+1))
else
  echo "FAIL  stale generated board/version truth was not rejected (exit $rc)" >&2
  sed 's/^/    | /' "$WORK/out" >&2
  fail=$((fail+1))
fi

# `.github#2238`: manifests digest skill bodies that projections can rewrite. A manifest check
# used to report current when it ran before its projection producer. Start from that stale input;
# check must fail closed, and write must establish projection-before-manifest order.
seed
sed -i '/GENERATED: fsgg-versions/{n;s/^/STALE /;}' "$WORK/tree/docs/architecture.md"
rc=0
python3 "$WORK/tree/scripts/generate-driver-manifest" --check >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] \
  && grep -Fq -- "STALE" "$WORK/out" \
  && grep -Fq -- "refusing to digest" "$WORK/out"; then
  echo "PASS  driver manifest refuses stale projection inputs before digesting them"
  pass=$((pass+1))
else
  echo "FAIL  driver manifest accepted stale projection inputs (exit $rc)" >&2
  sed 's/^/    | /' "$WORK/out" >&2
  fail=$((fail+1))
fi

rc=0
python3 "$WORK/tree/scripts/generate-driver-manifest" --write >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 0 ] \
  && bash "$WORK/tree/scripts/generate-projections" --check >>"$WORK/out" 2>&1 \
  && python3 "$WORK/tree/scripts/generate-driver-manifest" --check >>"$WORK/out" 2>&1; then
  echo "PASS  driver manifest write projects before digesting"
  pass=$((pass+1))
else
  echo "FAIL  driver manifest write did not establish projection-before-manifest order" >&2
  sed 's/^/    | /' "$WORK/out" >&2
  fail=$((fail+1))
fi

# `.github#2333`: nothing at the point of editing a skill distinguished FS.GG.Kit-tracked skills from
# FS.GG.Drivers-tracked ones, so `.github#2135` misdiagnosed a drive-board/work-board change as a
# FS.GG.Kit release and tagged a permanently empty `kit/v0.47.0` (the tag is immutable). `--which`
# answers that question from the same two committed sources `--write` reads
# (registry/repos.yml's `kit:` rows, this emitter's own `DRIVERS` list) — never a third, hand-copied
# roster. Exercise a skill id known to be `kit:`-tracked, one known to be
# `driver-skill-manifest.json`-tracked, and one in neither population: each must answer differently,
# and the "neither" case must still print something rather than staying silent.
expect_which() {
  local label="$1" skill_id="$2" want_stdout="$3" want_rc="$4"
  local rc=0
  python3 "$WORK/tree/scripts/generate-driver-manifest" --which "$skill_id" \
    --catalog-root "$WORK/tree" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq "$want_rc" ] && [ "$(cat "$WORK/out")" = "$want_stdout" ]; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted stdout '$want_stdout' exit $want_rc; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

# `.github#2339`: `--which` used to answer `FS.GG.Drivers` for EVERY id in `DRIVERS` — driver-manifest
# MEMBERSHIP — when the real question is package membership, which `FS.GG.Drivers.csproj`/
# `stage-drivers.py` answer by staging only `scope: driver` rows. `drive-board` and
# `publishing-and-deployment` are both `scope: operator` (ADR-0057, "materialized NOWHERE") and ship in
# no package at all; asserting they resolve to `FS.GG.Drivers` (the pre-fix expectation below) is the
# exact defect this item exists to remove — a test asserting manifest membership passes today and
# proves nothing. `work-board` is the `scope: driver` sibling used as the one true packed-and-answered
# case, so this block now asserts against the packing rule instead: one id per scope class.
#
# The packing rule itself — never restated here, only read — is `stage-drivers.py`'s own literal.
# Confirm it is what the assertions below assume before relying on it, so a future change to WHICH
# scope gets packed fails this line first, loudly, rather than leaving `--which`'s test coverage
# silently checking the wrong thing.
packed_scope="$(python3 - "$ROOT/src/FS.GG.Drivers/stage-drivers.py" <<'PY'
import re
import sys
text = open(sys.argv[1], encoding="utf-8").read()
m = re.search(r'DELIVERED_SCOPES\s*=\s*\{\s*"([a-z]+)"\s*\}', text)
print(m.group(1) if m else "")
PY
)"
if [ "$packed_scope" = "driver" ]; then
  echo "PASS  packing rule (stage-drivers.py DELIVERED_SCOPES) stages scope: driver, as --which assumes"
  pass=$((pass+1))
else
  echo "FAIL  packing rule scope literal is '$packed_scope', not 'driver' — the assertions below assume it" >&2
  fail=$((fail+1))
fi

# Exit code: 0 means "a package ships this, a release may be owed" and ONLY `FS.GG.Kit`/`FS.GG.Drivers`
# get it. `operator-scope` shares exit 1 with `neither` DELIBERATELY (.github#2339 round 1) — this
# repo's dominant idiom loads every small integer above 1 with an "abstain / no verdict" meaning
# `operator-scope` does not have, so it gets no exit code of its own; stdout alone carries the
# distinction AC-2 requires. These exact-equality checks (stdout AND exit code) also guard the
# regression the round-1 critique named: if `operator-scope` ever again returns exit 2, these two
# cases redden on the exit code even though stdout still reads correctly.
expect_which "kit-tracked (packed in FS.GG.Kit)" "check-board" "FS.GG.Kit" 0
expect_which "scope: driver, packed in FS.GG.Drivers per the packing rule" "work-board" "FS.GG.Drivers" 0
expect_which "scope: operator ships in NO package (ADR-0057) — distinct stdout, exit 1 (not a fresh code)" \
  "drive-board" "operator-scope" 1
expect_which "publishing-and-deployment is itself scope: operator, not scope: driver — no release owed" \
  "publishing-and-deployment" "operator-scope" 1
expect_which "an id in no population at all is reported, never silent" "spectre-console" "neither" 1
expect_which "an unknown id is reported, never silent, and never confused with operator-scope" \
  "not-a-real-skill" "neither" 1

# Prove the fix by breaking the subject (.github#2339 acceptance criterion 5): flip a fixture row's
# `scope` in the copied emitter and confirm `--which`'s answer MOVES with it. This is the inversion the
# previous review (`.github#2333`) never ran — it proved `--which` follows its sources, never that
# reading `scope` (rather than mere id membership) is what makes the answer correct.
seed
python3 - "$WORK/tree/scripts/generate-driver-manifest" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
needle = '''    {
        "id": "drive-board",
        "scope": "operator",
        "supplied-by": ".claude/skills/drive-board",
        "materializes-when": "false",
    },'''
if needle not in text:
    raise SystemExit("fixture DRIVERS row for drive-board missing or reformatted")
path.write_text(text.replace(needle, needle.replace('"scope": "operator"', '"scope": "driver"'), 1))
PY
expect_which "flipping drive-board's scope operator->driver moves the answer to FS.GG.Drivers" \
  "drive-board" "FS.GG.Drivers" 0

seed
python3 - "$WORK/tree/scripts/generate-driver-manifest" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
needle = '''    {
        "id": "work-board",
        "scope": "driver",
        "supplied-by": ".claude/skills/work-board",
        "materializes-when": "always",
    },'''
if needle not in text:
    raise SystemExit("fixture DRIVERS row for work-board missing or reformatted")
path.write_text(text.replace(needle, needle.replace('"scope": "driver"', '"scope": "operator"'), 1))
PY
expect_which "flipping work-board's scope driver->operator moves the answer off FS.GG.Drivers" \
  "work-board" "operator-scope" 1

# `.github#2136`: release inventory is a registry projection, not a sentence that happens to be true
# today. Each registry mutation below must make that generated region stale without touching the skill.
expect_projection_stale() {
  local label="$1" evidence="$2"
  local rc=0
  bash "$WORK/tree/scripts/generate-projections" --check >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted generated diff containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

seed
python3 - "$WORK/tree/registry/dependencies.yml" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
repo_anchor = '  net:        { name: FS.GG.Net,'
repo_start = text.find(repo_anchor)
if repo_start < 0:
    raise SystemExit("fixture producer roster anchor missing")
repo_end = text.find("\n", repo_start)
text = text[:repo_end + 1] + '  fixture-producer: { name: FS.GG.Fixture, role: "fixture release producer" }\n' + text[repo_end + 1:]
addition = '''  - id: fixture-release-tool
    version: "1.0.0"
    package-version: "1.0.0"
    owner: fixture-producer
    surface: "fixture package-bearing producer"
    consumers: []
'''
needle = "  - id: new-sdd-workspace\n"
if needle not in text:
    raise SystemExit("fixture insertion anchor missing")
path.write_text(text.replace(needle, addition + needle, 1))
PY
expect_projection_stale "adding a package producer changes inventory membership and producer count" \
  "fixture-producer:1.0.0"

seed
python3 - "$WORK/tree/registry/dependencies.yml" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
start = text.find("  - id: fs-gg-net\n")
if start < 0:
    raise SystemExit("fixture package producer missing")
end = text.find("\n  - id:", start + 1)
if end < 0:
    raise SystemExit("fixture package producer has no bounded successor")
path.write_text(text[:start] + text[end + 1:])
PY
expect_projection_stale "removing a package producer changes inventory membership" "fs-gg-net"

seed
python3 - "$WORK/tree/registry/dependencies.yml" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
start = text.find("  - id: game-scene-adapter\n")
if start < 0:
    raise SystemExit("fixture coherent-set producer missing")
package = text.find('    package-version: "0.13.0"', start)
if package < 0:
    raise SystemExit("fixture coherent-set package version missing")
path.write_text(text[:package] + text[package:].replace('    package-version: "0.13.0"', '    package-version: "0.13.1"', 1))
PY
expect_projection_stale "splitting one producer's package versions splits its coherent set" "game:0.13.1"

seed
python3 - "$WORK/tree/registry/dependencies.yml" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
start = text.find("  - id: fs-gg-audio\n")
if start < 0:
    raise SystemExit("fixture audio producer missing")
package = text.find('    package-version: "0.5.0"', start)
if package < 0:
    raise SystemExit("fixture audio package version missing")
path.write_text(text[:package] + text[package:].replace('    package-version: "0.5.0"', '    package-version: "0.5.1"', 1))
PY
expect_projection_stale "changing a published package version changes its real release grouping" "audio:0.5.1"

seed
bash "$WORK/tree/scripts/generate-projections"
if grep -Fq -- 'audio:0.5.0' "$WORK/tree/.claude/skills/publishing-and-deployment/SKILL.md" \
  && grep -Fq -- 'net:0.5.0' "$WORK/tree/.claude/skills/publishing-and-deployment/SKILL.md"; then
  echo "PASS  same-version Audio and Net remain independent coherent sets"
  pass=$((pass+1))
else
  echo "FAIL  same-version Audio and Net collapsed into one coherent set" >&2
  fail=$((fail+1))
fi

# A machine declaration outside its managed region is a second source even when its value happens to
# agree. The semantic gate must reject that duplicate before a later value change splits the skills.
seed
for runtime in .claude .agents; do
  printf '\n<!-- fsgg:wave-model:v1 waves=2 implementer-slots-per-wave=3 review-slots=2 consolidation-threshold=3 -->\n' \
    >>"$WORK/tree/$runtime/skills/drive-board/references/host-loop.md"
done
expect_rejection "hand-authored duplicate wave literals are rejected" \
  "drive-board: hand-authored duplicate of generated wave policy"

# --- .github#2510: routed dispatch must resolve from THIS repo ---------------------------------
#
# Each case inverts exactly one clause of agent-definition-coverage.py against an otherwise-pristine
# seeded tree. Every one of them was green before the gate existed — `.claude/agents/` did not exist
# at all — which is the point: the whole directory was the defect, so an unmutated tree is the only
# baseline that can pass.

seed_agents
rm "$WORK/tree/.claude/agents/fsgg-critic-best.md"
expect_agent_rejection "a route named by a skill with no definition shipped" \
  "no .claude/agents/fsgg-critic-best.md, so a 'best'-routed dispatch cannot resolve"

seed_agents
rm -rf "$WORK/tree/.claude/agents"
expect_agent_rejection "no agent directory at all is the original defect" \
  ".claude/agents/ does not exist, so no routed dispatch can resolve here"

# The silent-downgrade shape: a definition that still resolves, at the wrong tier. `Agent` carries
# `model`, so this one is at least expressible elsewhere; the effort case below is not.
seed_agents
python3 - "$WORK/tree/.claude/agents/fsgg-worker-best.md" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
if "\nmodel: opus\n" not in text:
    raise SystemExit("fixture model line missing")
path.write_text(text.replace("\nmodel: opus\n", "\nmodel: sonnet\n", 1))
PY
expect_agent_rejection "a definition downgraded below its routing table" \
  "fsgg-worker-best.md declares model 'sonnet', but the 'best' routing table mandates 'opus'"

seed_agents
python3 - "$WORK/tree/.claude/agents/fsgg-critic-normal.md" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
if "\neffort: high\n" not in text:
    raise SystemExit("fixture effort line missing")
path.write_text(text.replace("\neffort: high\n", "\n", 1))
PY
expect_agent_rejection "a definition that drops the effort only frontmatter can set" \
  "fsgg-critic-normal.md declares effort None, but the 'normal' routing table mandates 'high'"

# AC3 verbatim: a skill naming a new fsgg-* type without shipping its definition fails rather than merges.
seed_agents
printf '\nA future round may be dispatched as `fsgg-critic-turbo`.\n' \
  >>"$WORK/tree/.claude/skills/pnext-item/references/independent-review.md"
expect_agent_rejection "a skill naming a type no route defines" \
  "names agent type 'fsgg-critic-turbo', which belongs to no route the board variants define"

seed_agents
printf -- '---\nname: fsgg-worker-turbo\ndescription: unreachable\nmodel: opus\neffort: high\n---\n' \
  >"$WORK/tree/.claude/agents/fsgg-worker-turbo.md"
expect_agent_rejection "a definition no route and no skill reaches" \
  ".claude/agents/fsgg-worker-turbo.md defines 'fsgg-worker-turbo', which no route and no checked-in skill names"

# Two variants sharing a route suffix must publish the same row; otherwise "the normal route" names
# two different tiers and no single definition can satisfy both.
seed_agents
python3 - "$WORK/tree/.claude/skills/work-board-normal/SKILL.md" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
if "| Claude Code | `sonnet` | `high` |" not in text:
    raise SystemExit("fixture routing row missing")
path.write_text(text.replace("| Claude Code | `sonnet` | `high` |", "| Claude Code | `opus` | `high` |", 1))
PY
expect_agent_rejection "two variants disagreeing about one route's tier" \
  "work-board-normal routes 'normal' to opus/high, but another variant routes it to sonnet/high"

# Repair 1 of this item's round-1 review. THE ESCAPE THIS CLOSES IS TWO-FACTOR, which is why the
# single-factor case above ("a skill naming a type no route defines") did not catch it: that case
# reds only because the scan FINDS the undefined type. Point the declared roots at directories that
# do not exist and the scan finds nothing at all, so the identical undefined type cleared a green
# exit 0 — a non-answer emitted as a positive. Both mutations are applied together here on purpose;
# reverting either half of the gate's root check turns this leg red.
seed_agents
printf '.claude/skills-renamed\n.agents/skills-renamed\n' >"$WORK/tree/.agent-skill-roots"
printf '\nA future round may be dispatched as `fsgg-critic-turbo`.\n' \
  >>"$WORK/tree/.claude/skills/pnext-item/references/independent-review.md"
expect_agent_rejection "declared skill roots that resolve to nothing clear every named type" \
  ".agent-skill-roots declares 2 skill root(s) but .claude/skills-renamed, .agents/skills-renamed does not exist"

# Repair 2 of the same review. Repair 1 guarded "the declared root is gone"; this is "the declared
# root EXISTS and is empty", which produces the identical confident exit 0 and the identical vacuous
# closure. It is also the shape a real migration produces, because a migration begins by mkdir-ing
# the new root — not by deleting the old one — so this leg, not the one above, is the sequence the
# repair-1 rationale actually described. The assertion it pins is on the MEASURED file count, so no
# third spelling of "the roots look fine but hold nothing" gets a fresh escape.
seed_agents
mkdir -p "$WORK/tree/.claude/skills-v2" "$WORK/tree/.agents/skills-v2"
printf '.claude/skills-v2\n.agents/skills-v2\n' >"$WORK/tree/.agent-skill-roots"
printf '\nA future round may be dispatched as `fsgg-critic-turbo`.\n' \
  >>"$WORK/tree/.claude/skills/pnext-item/references/independent-review.md"
expect_agent_rejection "declared skill roots that exist but hold no skills clear every named type" \
  "scanned 0 skill file(s) under .claude/skills-v2, .agents/skills-v2"

# Epic #266's own class: a gate that does not run when its subject changes is green and wrong.
seed_agents
python3 - "$WORK/tree/.github/workflows/skill-quality.yml" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
if '      - ".claude/agents/**"\n' not in text:
    raise SystemExit("fixture trigger line missing")
path.write_text(text.replace('      - ".claude/agents/**"\n', "", 1))
PY
expect_agent_rejection "the gate no longer triggers on its own subject" \
  "does not list '.claude/agents/**' under pull_request.paths"

# --- .github#2666: agent-facing prose must not reach for a recency-based comment edit -----------
#
# `gh pr comment --edit-last` edits by ACCOUNT and this whole fleet shares one, so on PR #2663 a
# worker rebinding its own obligation declaration overwrote an independent critic's findings comment.
# The `fsgg:review-decision/v2` record survived on ordering luck alone. These are the committed
# inversions: each one is a real mutation of a seeded tree, run through the gate's own entry point.

# The defect verbatim: an instruction telling an agent to edit by recency.
seed_agents
printf '\nIf the head moved, just `gh pr comment --edit-last` to rebind your declaration.\n' \
  >>"$WORK/tree/.claude/skills/check-board/SKILL.md"
expect_recency_rejection "an instruction to edit a comment by recency is rejected" \
  "names a recency-based comment edit ('edit-last') outside the canonical hazard statement"

# Criterion 3 of the row: the statement must be UNAVOIDABLE, which means it cannot be quietly
# deleted. Removing it from one definition leaves that route's agents reading nothing about it.
seed_agents
python3 - "$WORK/tree" <<'PY'
import importlib.util
import sys
from pathlib import Path

root = Path(sys.argv[1])
spec = importlib.util.spec_from_file_location(
    "gate", root / "tests/skill-quality/recency-comment-edit.py"
)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
path = root / ".claude/agents/fsgg-worker-normal.md"
text = path.read_text()
if gate.CANONICAL + "\n\n" not in text:
    raise SystemExit("fixture: fsgg-worker-normal.md does not carry the canonical statement")
path.write_text(text.replace(gate.CANONICAL + "\n\n", "", 1))
PY
expect_recency_rejection "a definition that drops the hazard statement is rejected" \
  ".claude/agents/fsgg-worker-normal.md carries the canonical recency-edit hazard statement 0 time(s)"

# Drift, not deletion. A definition that keeps a REWORDED near-copy still names the flag, and the
# strip only removes byte-identical copies — so the residue carries the token and the prohibition
# leg reds. This is why the gate needs no diff between the four files: divergence cannot survive it.
seed_agents
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1]) / ".claude/agents/fsgg-critic-normal.md"
text = path.read_text()
if "Editing by recency is never safe here." not in text:
    raise SystemExit("fixture: canonical closing sentence missing")
path.write_text(text.replace("Editing by recency is never safe here.", "Prefer explicit ids.", 1))
PY
expect_recency_rejection "a definition whose copy of the statement drifted is rejected" \
  ".claude/agents/fsgg-critic-normal.md names a recency-based comment edit"

# Vacuous green (.github#2551): the sweep's subject is source text, so a root that exists and holds
# nothing clears every mention by default. Three mutations land together, and the third is what makes
# this leg honest. The violating instruction is real and sits in a directory the declaration no longer
# points at; the renamed roots are ALSO taught to the workflow, so the trigger leg is satisfied and
# cannot stand in for the count. Without that third mutation this case reds on the trigger message
# instead and proves nothing about vacuity — measured, not assumed: deleting the per-root count with
# the workflow left alone still exits 1, on the wrong finding.
seed_agents
mkdir -p "$WORK/tree/.claude/skills-v2" "$WORK/tree/.agents/skills-v2"
printf '.claude/skills-v2\n.agents/skills-v2\n' >"$WORK/tree/.agent-skill-roots"
python3 - "$WORK/tree/.github/workflows/skill-quality.yml" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
old = '      - ".claude/skills/**"\n'
if text.count(old) != 2:
    raise SystemExit("fixture: expected the skills trigger under both pull_request and push")
path.write_text(text.replace(old, old + '      - ".claude/skills-v2/**"\n      - ".agents/skills-v2/**"\n'))
PY
printf '\nIf the head moved, just `gh pr comment --edit-last` to rebind your declaration.\n' \
  >>"$WORK/tree/.claude/skills/check-board/SKILL.md"
expect_recency_rejection "a declared root that exists but holds no prose sweeps nothing" \
  "scanned 0 file(s) under .claude/skills-v2"

# The neighbouring shape: the declaration points at a directory that is not there at all.
seed_agents
printf '.claude/skills-renamed\n.agents/skills-renamed\n' >"$WORK/tree/.agent-skill-roots"
expect_recency_rejection "a declared root that resolves to nothing leaves that surface unswept" \
  ".claude/skills-renamed, .agents/skills-renamed does not exist"

# Epic #266's class again, aimed at this gate's OWN source: drop `tests/skill-quality/**` and a
# change to the scanner itself would never be checked by the workflow that runs it.
seed_agents
python3 - "$WORK/tree/.github/workflows/skill-quality.yml" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
text = path.read_text()
if '      - "tests/skill-quality/**"\n' not in text:
    raise SystemExit("fixture trigger line missing")
path.write_text(text.replace('      - "tests/skill-quality/**"\n', "", 1))
PY
expect_recency_rejection "the gate no longer triggers on its own source" \
  "does not list 'tests/skill-quality/**' under pull_request.paths"

if [ "$fail" -ne 0 ]; then
  echo "skill-quality fixture: $fail failure(s), $pass pass(es)" >&2
  exit 1
fi
echo "skill-quality fixture: all $pass rejection cases passed"
