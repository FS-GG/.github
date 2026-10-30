#!/usr/bin/env bash
# Fixture for scripts/check-scheduled-engine-provisioning.py (Refs #4730, main health).
#
# Offline and static: the gate reads the working tree only, so there is no transport to fake. Every
# negative leg asserts the REASON (workflow, job and step named), not merely a non-zero exit, so a
# gate that failed for an unrelated reason cannot pass this suite (the #266 vacuous-failure trap).
#
# The headline leg is REGRESSION: the REAL coord-board-archive.yml and projects-audit.yml from this
# working tree, with the engine-build step removed, are the two files that were red on every
# scheduled run from 2026-08-16. If this gate had existed on 2026-08-15 it would have refused 77938c5a.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-scheduled-engine-provisioning.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/sched-engine-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root>
expect() {
  local name="$1" want="$2" needle="$3" root="$4" out rc=0
  out="$(python3 "$TOOL" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else ok "$name"; fi
}

# tree <dir> — synthetic tree with the three scripts the legs need.
tree() {
  mkdir -p "$1/.github/workflows" "$1/scripts"
  printf '#!/usr/bin/env bash\ncoord=${FSGG_COORD_BIN:-"$(dirname "$0")/fsgg-coord"}\n"$coord" graphql x\n' >"$1/scripts/audit.sh"
  printf '#!/usr/bin/env python3\nimport os\nCOORD = os.environ.get("FSGG_COORD_BIN") or "scripts/fsgg-coord"\n' >"$1/scripts/sweep.py"
  # prose only: mentions the shim in a docstring and never resolves it
  printf '#!/usr/bin/env python3\n"""Runs before `scripts/fsgg-coord landable` does."""\nprint(1)\n' >"$1/scripts/prose.py"
  echo "$1"
}
BUILD='      - run: dotnet build src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj -c Release'
# wf <root> <file> <trigger-yaml> <steps…>
wf() {
  local root="$1" file="$2" on="$3"; shift 3
  { echo "name: w"; echo "on: $on"; echo "jobs:"; echo "  j:"; echo "    runs-on: ubuntu-latest"
    echo "    timeout-minutes: 5"; echo "    steps:"; printf '%s\n' "$@"; } >"$root/.github/workflows/$file"
}

# 1. REGRESSION on the real files ---------------------------------------------------------------
expect "real tree: every schedule-only engine job builds first" 0 "OK" "$REPO_ROOT"
R="$(tree "$WORK/regress")"
mkdir -p "$R/scripts"; cp "$REPO_ROOT"/scripts/projects-audit.sh "$REPO_ROOT"/scripts/coord-board-archive.py "$R/scripts/"
cp "$REPO_ROOT/scripts/fsgg-coord" "$R/scripts/"
python3 - "$REPO_ROOT" "$R" <<'PY'
import sys, yaml, pathlib
src, dst = map(pathlib.Path, sys.argv[1:3])
for name in ("coord-board-archive.yml", "projects-audit.yml"):
    text = (src / ".github/workflows" / name).read_text()
    doc = yaml.safe_load(text)
    job = next(iter(doc["jobs"].values()))
    kept = [s for s in job["steps"] if "dotnet" not in (s.get("run", "") + s.get("uses", ""))]
    assert len(kept) < len(job["steps"]), f"{name}: no build step to strip - fixture would be vacuous"
    job["steps"] = kept
    (dst / ".github/workflows" / name).write_text(yaml.safe_dump(doc))
PY
expect "regression: coord-board-archive without the build is refused" 1 "coord-board-archive.yml [archive]" "$R"
expect "regression: projects-audit without the build is refused"      1 "projects-audit.yml [audit]" "$R"

# 2. synthetic legs -----------------------------------------------------------------------------
T="$(tree "$WORK/a")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      - run: scripts/audit.sh --x"
expect "schedule-only, engine script, no build: refused" 1 "a.yml [j]" "$T"
T="$(tree "$WORK/b")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "$BUILD" "      - run: scripts/audit.sh --x"
expect "build first: clean" 0 "OK" "$T"
T="$(tree "$WORK/c")"; wf "$T" a.yml '{ workflow_dispatch: }' "      - run: scripts/audit.sh --x" "$BUILD"
expect "build AFTER first use: refused" 1 "a.yml [j]" "$T"
T="$(tree "$WORK/d")"; wf "$T" a.yml '{ workflow_dispatch: }' "      - run: scripts/fsgg-coord graphql project-id FS-GG 1"
expect "direct scripts/fsgg-coord call, no build: refused" 1 "a.yml [j]" "$T"
T="$(tree "$WORK/e")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      - run: python3 scripts/sweep.py"
expect "python script resolving FSGG_COORD_BIN, no build: refused" 1 "a.yml [j]" "$T"
T="$(tree "$WORK/f")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      - env: { FSGG_COORD_ENGINE_BIN: /x/y }" "        run: scripts/audit.sh"
expect "explicit FSGG_COORD_ENGINE_BIN counts as provisioned" 0 "OK" "$T"
T="$(tree "$WORK/g")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      - run: dotnet tool restore" "      - run: scripts/audit.sh"
expect "dotnet tool restore counts as provisioned" 0 "OK" "$T"
T="$(tree "$WORK/h")"; wf "$T" a.yml '{ pull_request: , schedule: [{ cron: "0 4 * * *" }] }' "      - run: scripts/audit.sh"
wf "$T" b.yml '{ workflow_dispatch: }' "      - run: echo hi"
expect "PR-triggered workflow is out of scope (it fails before merge)" 0 "OK" "$T"
T="$(tree "$WORK/i")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      - run: python3 scripts/prose.py"
expect "a docstring mention of the shim is not an engine call" 0 "OK" "$T"
T="$(tree "$WORK/j")"; wf "$T" a.yml '{ schedule: [{ cron: "0 4 * * *" }] }' "      # scripts/audit.sh would need it" "      - run: |" "          # scripts/audit.sh" "          echo hi"
expect "a comment naming an engine script is not a call" 0 "OK" "$T"

# 3. no verdict ---------------------------------------------------------------------------------
T="$WORK/empty"; mkdir -p "$T/.github/workflows" "$T/scripts"
expect "no workflows: no verdict" 3 "nothing to audit" "$T"
T="$(tree "$WORK/k")"; wf "$T" a.yml '{ pull_request: }' "      - run: echo hi"
expect "only pre-merge workflows: nothing examined is no verdict" 3 "examining nothing" "$T"
T="$(tree "$WORK/l")"; printf 'on: [\n' >"$T/.github/workflows/a.yml"
expect "unparsable workflow: no verdict" 3 "not parsable" "$T"

echo; echo "passed=$pass failed=$failcount"
[ "$failcount" -eq 0 ]
