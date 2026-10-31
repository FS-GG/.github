#!/usr/bin/env bash
# CAN THE CONSTRUCTOR-IDENTITY MUTATION MATRIX STILL BE RUN? (FS-GG/.github#5113)
#
# The legs in specs.yml mutate V2ConstructorClosure.fs, V2Preflight.fs and V2EnrollmentCommands.fs by EXACT SOURCE TEXT, so any edit there can
# leave a leg matching nothing, which the sweep would grade NOT MEASURED. This checks, in seconds and on every PR,
# that each leg's `find` still matches and its anchor producer exists. It runs NO mutation: green means the matrix
# is runnable, never that a guard was measured (#266). Same mechanism as tests/coord-engine-mutation/run.sh.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

SPECS="$HERE/specs.yml"
[ -f "$SPECS" ] || { echo "FAIL  the corpus is missing: $SPECS"; echo "constructor-identity mutation specs: 0 passed, 1 failed"; exit 1; }

REPORT="$(mktemp)"
python3 - "$ROOT" "$SPECS" >"$REPORT" 2>&1 <<'PY'
import sys, pathlib
root = pathlib.Path(sys.argv[1])
sys.path.insert(0, str(root / "scripts"))
from lib.mutation import load_specs, SpecError

try:
    specs = load_specs(pathlib.Path(sys.argv[2]), root)
except SpecError as e:
    # A REFUSED SPEC IS THE LOUDEST RESULT HERE, not the quietest. `load_specs` refuses precisely the
    # defects that yield a confidently wrong answer rather than a cautious one — chief among them an
    # anchor produced by the file the mutation edits (#1794's own defect, and #1825's subject).
    print(f"REFUSED\t{e}")
    raise SystemExit(0)

for m in specs:
    target = root / m.target
    producer = root / m.anchor.produced_by
    if not target.is_file():
        print(f"MISSING-TARGET\t{m.id}\t{m.target}"); continue
    if not producer.is_file():
        print(f"MISSING-PRODUCER\t{m.id}\t{m.anchor.produced_by}"); continue
    n = target.read_text(encoding="utf-8").count(m.find)
    print(f"COUNT\t{m.id}\t{n}\t{m.occurrences}\t{m.target}")
PY

if grep -q '^REFUSED' "$REPORT"; then
  bad "the corpus is REFUSED by scripts/lib/mutation.py — it could not produce a measurement" "$(cat "$REPORT")"
elif ! grep -q '^COUNT' "$REPORT"; then
  bad "the corpus loader produced no per-leg report at all — nothing was checked" "$(cat "$REPORT")"
else
  ok "tests/coord-constructor-identity-mutation/specs.yml LOADS — every anchor is independent of its mutation target (#1794/#1825 point 3)"
  while IFS=$'\t' read -r kind id a b target; do
    case "$kind" in
      COUNT)
        if [ "$a" = "$b" ]; then
          ok "$id: its \`find\` still matches $b time(s) in $target — the leg is runnable"
        else
          # NOT a pass and NOT a gate failing: the leg cannot be measured until somebody re-points it.
          bad "$id: ANCHOR PROBLEM — \`find\` matches $a time(s) in $target, expected $b. The source moved under this leg; it would grade NOT MEASURED (#1825 AC3). Re-point the \`find\` at the guard it defends, or delete the leg with a reason." ""
        fi ;;
      MISSING-TARGET)   bad "$id: mutation target $a does not exist — the leg cannot be run" "" ;;
      MISSING-PRODUCER) bad "$id: anchor producer $a does not exist — the leg has no witness that it ran" "" ;;
    esac
  done < <(grep -E '^(COUNT|MISSING-)' "$REPORT")
fi
rm -f "$REPORT"

# The command the matrix is actually run with must exist and be runnable, or the corpus documents a
# sweep nobody can start.
[ -x "$HERE/leg.sh" ] && ok "tests/coord-constructor-identity-mutation/leg.sh is present and executable — the sweep has a command to run" \
  || bad "tests/coord-constructor-identity-mutation/leg.sh is missing or not executable" ""

echo
echo "constructor-identity mutation specs: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::constructor-identity mutation: the matrix is NOT runnable as filed"; exit 1; }
echo "runnable — and that is all this says. Nothing was mutated and no gate was measured (#266)."
