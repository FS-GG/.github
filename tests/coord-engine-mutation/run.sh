#!/usr/bin/env bash
# CAN THE `.github#1794` MATRIX STILL BE RUN? — the cheap gate that keeps the expensive one honest.
#
# WHY THIS EXISTS AND WHY IT IS NOT THE MATRIX ITSELF. Running the matrix is about 70 minutes: eleven legs, each
# an engine rebuild plus 838 unit assertions plus the 641-assertion parity corpus, twice (control, then
# mutant). That is not a per-PR gate and pretending otherwise would get it disabled within a week.
#
# But a mutation corpus that no workflow touches is exactly the artefact `.github#1582` spent 30 gates
# measuring — present, plausible, and never executed. The failure mode is specific and it is silent: the
# `find:` strings are EXACT TEXT from `Reads.fs`, `Client.fs`, `Scan.fs`, `Transport.fs` and the parity
# fixture. Any edit to those files — a reword, a reindent, a refactor that never touched the guard — can
# leave a `find:` matching ZERO times. `scripts/lib/mutation.py` grades that `NOT_MEASURED` with a reason,
# which is honest, but it only says so to whoever runs the 50-minute sweep, and by then the source moved
# months ago and nobody knows which behaviour the leg was defending.
#
# So this is `.github#1825` AC3 mechanised: *a leg whose anchor no longer matches (the source moved under
# it) is reported and repaired, never silently skipped* — checked in seconds, on every PR that touches
# either the engine or this corpus, while the person who moved the source is still holding it.
#
# WHAT IT DOES NOT DO. It runs no mutation and grades no gate. A green here means the matrix is RUNNABLE,
# never that anything was measured — `#266`, and the distinction this whole line of work is about.
#
# DO NOT RUN IT WHILE A SWEEP IS IN FLIGHT. `scripts/gate-mutate.py` holds a mutation applied for the
# duration of each mutant run, so a concurrent check reads a target that is deliberately edited and
# reports an ANCHOR PROBLEM that is not one. Observed while writing this file, which is the only reason it
# is written down. CI never hits it (one job, clean tree); a human running both at once will.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

SPECS="$HERE/specs.yml"
[ -f "$SPECS" ] || { echo "FAIL  the corpus is missing: $SPECS"; echo "coord-engine-mutation specs: 0 passed, 1 failed"; exit 1; }

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
  ok "tests/coord-engine-mutation/specs.yml LOADS — every anchor is independent of its mutation target (#1794/#1825 point 3)"
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
[ -x "$HERE/leg.sh" ] && ok "tests/coord-engine-mutation/leg.sh is present and executable — the sweep has a command to run" \
  || bad "tests/coord-engine-mutation/leg.sh is missing or not executable" ""

echo
echo "coord-engine-mutation specs: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine-mutation: the #1794 matrix is NOT runnable as filed"; exit 1; }
echo "runnable — and that is all this says. Nothing was mutated and no gate was measured (#266)."
