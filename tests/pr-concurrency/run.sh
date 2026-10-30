#!/usr/bin/env bash
# Fixture for scripts/check-pr-concurrency.py (.github#4730 CI load). Offline, credential-free.
#
# CAUSAL CONTROL. The gate derives "a superseded PR head is cancellable and a main push is not" from the
# workflow text by modelling GitHub's group semantics. This fixture proves it can say NO: it copies the
# real governed workflows, applies each of the mutations below to EACH one (a generator, so a newly
# governed workflow is mutated automatically), and demands the gate refuses every mutant with the reason
# that mutation introduces — not merely a non-zero exit (tests/feed-coherence/run.sh:10, #266).
#
#   M1 delete the concurrency block               -> "no workflow-level `concurrency:` mapping"
#   M2 cancel-in-progress true (unconditional)    -> "cancel-in-progress is True"
#   M3 cancel-in-progress false (never cancels)   -> MODEL: a PR push does NOT cancel the superseded run
#   M4 fallback github.ref (Root's literal key)   -> MODEL: a main push's PENDING run IS dropped
#   M5 group without the PR number                -> MODEL: a PR push does NOT cancel the superseded run
#   M6 group keyed on another workflow's name     -> "concurrency.group is"
#   M7 an extra concurrency key                   -> "unexpected concurrency key"
#   M8 `<name>-${{ github.ref }}` + cancel true   -> MODEL: a main push IS cancelled (the shape adr-coherence uses)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
TOOL="$ROOT/scripts/check-pr-concurrency.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/pr-concurrency-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A. The real tree is clean.
rc=0; out="$(python3 "$TOOL" --root "$ROOT" 2>&1)" || rc=$?
if [ "$rc" = 0 ]; then ok; echo "PASS  A1 the real governed workflows are clean: $out"; else bad "A1 the real tree is clean" "$out"; fi

# B. Mutant generator: N workflows x 8 mutations.
mapfile -t NAMES < <(python3 "$TOOL" --list)
[ "${#NAMES[@]}" -gt 0 ] || { echo "FAIL  no governed workflows listed — a gate that governs nothing is not a clean gate"; exit 1; }

mutants=0
for name in "${NAMES[@]}"; do
  src="$ROOT/.github/workflows/$name.yml"
  for m in M1 M2 M3 M4 M5 M6 M7 M8; do
    d="$WORK/$name-$m"; mkdir -p "$d/.github/workflows"
    # every governed file must exist in the mutant root, so copy them all and mutate only one
    for other in "${NAMES[@]}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
    python3 - "$d/.github/workflows/$name.yml" "$m" "$name" <<'PY'
import re, sys
path, m, name = sys.argv[1:4]
t = open(path, encoding="utf-8").read()
block = re.search(r"^concurrency:\n(?:  .*\n)+", t, re.M)
assert block, f"{path}: the clean tree has no concurrency block to mutate"
b = block.group(0)
new = {
  "M1": "",
  "M2": b.replace("${{ github.event_name == 'pull_request' }}", "true"),
  "M3": b.replace("${{ github.event_name == 'pull_request' }}", "false"),
  "M4": b.replace("github.sha", "github.ref"),
  "M5": re.sub(r"group: .*", f"group: {name}-${{{{ github.sha }}}}", b),
  "M6": re.sub(r"group: .*", "group: some-other-workflow-${{ github.event.pull_request.number || github.sha }}", b),
  "M7": b + "  queue: max\n",
  "M8": re.sub(r"group: .*", f"group: {name}-${{{{ github.ref }}}}", b).replace("${{ github.event_name == 'pull_request' }}", "true"),
}[m]
assert new != b or m == "M1" and new == "", f"{m} did not change {path}"
open(path, "w", encoding="utf-8").write(t.replace(b, new, 1))
PY
    want=""
    case "$m" in
      M1) want="no workflow-level \`concurrency:\` mapping" ;;
      M2) want="cancel-in-progress is True" ;;
      M3) want="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      M4) want="model says it IS true that a main push does not drop the previous main push's PENDING run" ;;
      M5) want="model says it is NOT true that a PR push (new head) cancels the superseded head's run" ;;
      M6) want="concurrency.group is" ;;
      M7) want="unexpected concurrency key" ;;
      M8) want="model says it IS true that a main push does not cancel the previous main push run" ;;
    esac
    rc=0; out="$(python3 "$TOOL" --root "$d" 2>&1)" || rc=$?
    mutants=$((mutants+1))
    key="${want#model says it IS true that }"; key="${key#model says it is NOT true that }"
    if [ "$rc" = 1 ] && printf '%s' "$out" | grep -F -- "$name.yml" | grep -qF -- "${key}"; then ok
    else bad "$name $m was not refused for the right reason (rc=$rc, want '$key')" "$(printf '%s' "$out" | head -5)"; fi
  done
done
[ "$failcount" != 0 ] || echo "PASS  B  $mutants mutant(s) over ${#NAMES[@]} governed workflow(s) were each refused for the reason that mutation introduces"

# C. A missing governed workflow is NO VERDICT (3), never a pass.
d="$WORK/missing"; mkdir -p "$d/.github/workflows"
for other in "${NAMES[@]:1}"; do cp "$ROOT/.github/workflows/$other.yml" "$d/.github/workflows/"; done
rc=0; python3 "$TOOL" --root "$d" >/dev/null 2>&1 || rc=$?
if [ "$rc" = 3 ]; then ok; echo "PASS  C1 a missing governed workflow is exit 3, not a pass"; else bad "C1 missing workflow must be exit 3" "rc=$rc"; fi

echo "pr-concurrency fixture — $((pass+failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" = 0 ] && echo "pr-concurrency fixture — OK"
exit "$failcount"
