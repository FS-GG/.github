#!/usr/bin/env bash
# Fixture for the registry-changelog gate (.github#1672, wiring repaired by .github#2515).
#
# TWO SUBJECTS, and the distinction is the whole point of this file.
#
#   1. THE CHECKER — scripts/check-registry-changelog.py. Pure, offline, argument-driven. Legs below
#      marked `script:` drive it directly. It was ALREADY correct and ALREADY tested this way.
#   2. THE WIRING — .github/workflows/registry-changelog.yml's `gate` step, which decides what the
#      checker is asked about. Legs marked `workflow:` extract that step's own `run:` block by a real
#      YAML parse and EXECUTE it against synthetic git repositories.
#
# Subject 2 exists because subject 1 being green proved nothing about production (.github#2515). The
# `gate` step used to hardcode `--changed registry/dependencies.yml --changed registry/CHANGELOG.md`,
# so the checker's co-change arm was constant-false in CI: a commit editing dependencies.yml with no
# CHANGELOG entry went green, while THIS fixture's `script:` leg for the same condition went red and
# nobody could tell. A gate whose fixture reaches an arm production cannot is the #266 green-by-absence
# shape, and testing only the callee is how a fixture stays green across a broken caller forever.
# So the wiring is now a first-class subject: revert the derivation and these legs go red.
#
# WHY EXTRACT-AND-RUN RATHER THAN ASSERT ON STRINGS. A grep for "--changed" would pass against any
# rewrite that spelled the defect differently, and would fail against a correct refactor. Running the
# real step is the only assertion whose subject is the step's BEHAVIOUR. The mechanism is
# tests/kit-auto-publish/run.sh's extract_escalation_step, and the YAML parse is deliberate:
# scripts/lib/extract-workflow-shell.py's header states why a `.yml` extension says nothing about
# which of its lines are shell.
#
# EVERY NEGATIVE LEG ASSERTS THE REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: a "must fail" test whose non-zero exit came from a path guard, or from the OTHER
# arm of the same checker, would pass against a gate broken in a completely different way. That trap
# is live here — the checker has two arms that both exit 1 — so the co-change legs deliberately leave
# `updated:` alone, making the date arm incapable of producing their red.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
TOOL="$ROOT/scripts/check-registry-changelog.py"
WORKFLOW="$ROOT/.github/workflows/registry-changelog.yml"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/registry-changelog-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
export GIT_AUTHOR_NAME=fixture GIT_AUTHOR_EMAIL=fixture@fs-gg.invalid
export GIT_COMMITTER_NAME=fixture GIT_COMMITTER_EMAIL=fixture@fs-gg.invalid

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# ---------------------------------------------------------------------------------------------
# Subject 1: the checker itself.
# ---------------------------------------------------------------------------------------------

# script_expect <name> <want-rc> <needle> -- <args...>
script_expect() {
  local name="$1" want="$2" needle="$3"; shift 3; [ "${1:-}" = "--" ] && shift
  local out rc=0
  out="$(python3 "$TOOL" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "script: $name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "script: $name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "script: $name"
  fi
}

# The live registry is the subject of these legs — they assert the committed tree really does satisfy
# the protocol, which is a different question from whether the checker can refuse.
LIVE="$WORK/live"; mkdir -p "$LIVE"
cp "$ROOT/registry/dependencies.yml" "$LIVE/dependencies.yml"
cp "$ROOT/registry/CHANGELOG.md" "$LIVE/CHANGELOG.md"
LIVE_ARGS=(--dependencies "$LIVE/dependencies.yml" --changelog "$LIVE/CHANGELOG.md")

script_expect "the live registry satisfies the protocol" 0 "registry changelog protocol holds" \
  -- "${LIVE_ARGS[@]}" --changed registry/dependencies.yml --changed registry/CHANGELOG.md
script_expect "dependencies.yml alone is refused" 1 "dependencies.yml changed without registry/CHANGELOG.md" \
  -- "${LIVE_ARGS[@]}" --changed registry/dependencies.yml

for marker in '-' '*' '+'; do
  sed -i "3i$marker **2000-01-01** — misplaced dated entry" "$LIVE/CHANGELOG.md"
  script_expect "a dated '$marker' entry above the Entries heading is refused" 1 "dated entry before Entries heading" \
    -- "${LIVE_ARGS[@]}" --changed registry/dependencies.yml --changed registry/CHANGELOG.md
  sed -i '3d' "$LIVE/CHANGELOG.md"
done

sed -i 's/^updated: ".*"/updated: "2000-01-01"/' "$LIVE/dependencies.yml"
script_expect "updated: that disagrees with the top entry is refused" 1 "!= top changelog date" \
  -- "${LIVE_ARGS[@]}" --changed registry/dependencies.yml --changed registry/CHANGELOG.md

# ---------------------------------------------------------------------------------------------
# Subject 2: the workflow's own `gate` step (.github#2515 AC4).
# ---------------------------------------------------------------------------------------------

GATE="$WORK/gate-step.sh"

# Extract the ONE `gate` step that invokes the checker, and materialize its `run:` block verbatim.
#
# Three refusals here, each of which reds this fixture rather than degrading into a skipped subject:
#   - the step is gone (someone deleted the derivation outright — AC4's named regression);
#   - there is more than one such step (a second, always-green invocation could otherwise be bolted
#     on beside the real one and this fixture would keep measuring only the first);
#   - the step interpolates a `${{ }}` expression into its script. That shape cannot be executed
#     here, and it should not be written anyway: architecture-map.yml:43 states the injection reason
#     for passing these values through `env:` instead. Refusing it keeps the step executable, which
#     is what keeps it testable.
python3 - "$WORKFLOW" "$GATE" <<'PY'
import sys
import yaml

wf_path, out_path = sys.argv[1], sys.argv[2]
with open(wf_path, encoding="utf-8") as fh:
    wf = yaml.safe_load(fh)

steps = ((wf.get("jobs") or {}).get("gate") or {}).get("steps") or []
runs = [s["run"] for s in steps
        if isinstance(s, dict) and isinstance(s.get("run"), str)
        and "check-registry-changelog.py" in s["run"]]

if not runs:
    sys.exit("::error::no step in registry-changelog.yml's `gate` job invokes "
             "check-registry-changelog.py — the gate's derivation step is GONE")
if len(runs) > 1:
    sys.exit(f"::error::{len(runs)} steps in the `gate` job invoke check-registry-changelog.py; "
             "this fixture measures the one production invocation, so there must be exactly one")
if "${{" in runs[0]:
    sys.exit("::error::the `gate` step interpolates a ${{ }} expression into its script — pass those "
             "values through `env:` (architecture-map.yml:43 states the injection reason), which is "
             "also what lets this fixture execute the real step")

with open(out_path, "w", encoding="utf-8") as out:
    out.write("#!/usr/bin/env bash\n")
    out.write(runs[0])
PY

# The MUTANT: the exact pre-fix defect, reintroduced textually into a copy of the REAL step, so
# gate-inversion evidence (pnext-item §3) is a permanent re-runnable leg rather than a one-time manual
# observation. It replaces the derivation — and only the derivation — with the literal pair the
# workflow used to hardcode, leaving every other line of the real step in place. Anchored to the real
# derivation line: if that line ever stops existing, this errors out and reds the fixture rather than
# silently mutating nothing and "passing".
#
# A missing anchor is recorded as a FAILING LEG rather than aborting this script. Aborting was tried
# and is worse: reverting the production derivation removes the anchor AND breaks the behavioural legs
# below, and an abort reports only the first, burying the headline finding ("the violating commit is
# green again") under a note about the mutation harness.
MUTANT="$WORK/gate-step-hardcoded.sh"
mutant_built=1
python3 - "$GATE" "$MUTANT" <<'PY' || mutant_built=0
import sys

src, dst = sys.argv[1], sys.argv[2]
out, replaced = [], False
for line in open(src, encoding="utf-8").read().splitlines(keepends=True):
    if line.lstrip().startswith('changed="$(git diff --name-only'):
        indent = line[:len(line) - len(line.lstrip())]
        out.append(f'{indent}changed="registry/dependencies.yml\nregistry/CHANGELOG.md"\n')
        replaced = True
    else:
        out.append(line)
if not replaced:
    sys.exit("::error::could not locate the gate step's changed-set derivation line to mutate back to "
             "the pre-.github#2515 defect — the mutation has no anchor, so its green means nothing")
open(dst, "w", encoding="utf-8").write("".join(out))
PY

# A synthetic repository carrying a MINIMAL registry and the real checker at its real relative path.
#
# Synthetic rather than a copy of registry/dependencies.yml on purpose: these legs are about the
# changed SET, not about the live registry's content (the `script:` legs above own that), and a
# fixture that mutates copies of the live files reds whenever an unrelated registry change lands.
new_repo() {
  local d="$1"
  mkdir -p "$d/registry" "$d/scripts"
  cp "$TOOL" "$d/scripts/check-registry-changelog.py"
  cat > "$d/registry/dependencies.yml" <<'YML'
# synthetic registry
updated: "2026-01-01"
repos:
  alpha: { name: Alpha }
YML
  cat > "$d/registry/CHANGELOG.md" <<'MD'
# Registry changelog

## Entries

- **2026-01-01** — synthetic base entry
MD
  echo "unrelated" > "$d/README.md"
  git -C "$d" init -q -b main
  git -C "$d" add -A
  git -C "$d" commit -qm base
}

commit_all() { git -C "$1" add -A && git -C "$1" commit -qm "$2" && git -C "$1" rev-parse HEAD; }

# workflow_expect <name> <script> <want-rc> <needle> <repo> <event> <base> <head>
workflow_expect() {
  local name="$1" script="$2" want="$3" needle="$4" repo="$5" event="$6" base="$7" head="$8"
  local out rc=0
  out="$(cd "$repo" && EVENT_NAME="$event" \
      PR_BASE_SHA="$base" PR_HEAD_SHA="$head" \
      PUSH_BEFORE="$base" PUSH_AFTER="$head" \
      bash "$script" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "workflow: $name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "workflow: $name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "workflow: $name"
  fi
}

CO_CHANGE="dependencies.yml changed without registry/CHANGELOG.md"
HOLDS="registry changelog protocol holds"

# --- The violating shape: dependencies.yml edited, no CHANGELOG entry. -------------------------
#
# `updated:` is deliberately LEFT ALONE, which makes this leg carry both AC2 and AC5 at once:
#   - the date arm CANNOT produce this red (updated: still matches the top entry), so the red is
#     attributable to the co-change arm and to nothing else;
#   - "green because updated: did not move" was the residual hole .github#2515 measured — the arm
#     that still worked in production only fired when the date moved.
# This reconstructs the shape of PR #2514's head f1d6218d775d278429cf6cea252b7d617ee3c723, the real
# commit that went green under the hardcoded wiring.
VIOL="$WORK/violating"; new_repo "$VIOL"
VIOL_BASE="$(git -C "$VIOL" rev-parse HEAD)"
printf '  beta: { name: Beta }\n' >> "$VIOL/registry/dependencies.yml"
VIOL_HEAD="$(commit_all "$VIOL" "edit dependencies.yml, no changelog entry, updated: untouched")"

workflow_expect "a dependencies.yml edit with no CHANGELOG entry reds the PR arm" \
  "$GATE" 1 "$CO_CHANGE" "$VIOL" pull_request "$VIOL_BASE" "$VIOL_HEAD"
workflow_expect "...and reds the push arm over the pushed range" \
  "$GATE" 1 "$CO_CHANGE" "$VIOL" push "$VIOL_BASE" "$VIOL_HEAD"

# GATE-INVERSION EVIDENCE. The same repository, the same real step, with only the derivation replaced
# by the literal pair the workflow hardcoded before .github#2515 — and it reports the protocol HOLDS.
# This is what makes the two legs above meaningful: it demonstrates, re-runnably, that their red comes
# from deriving the changed set and not from anything else in the step.
if [ "$mutant_built" -eq 1 ]; then
  workflow_expect "the pre-.github#2515 hardcoded wiring passes that same violating commit" \
    "$MUTANT" 0 "$HOLDS" "$VIOL" pull_request "$VIOL_BASE" "$VIOL_HEAD"
else
  bad "workflow: the gate step no longer derives its changed set (the mutation has no anchor) — see the ::error:: above"
fi

# --- The controlled counterparts: the fix is not "always red". ---------------------------------
CTRL="$WORK/control"; new_repo "$CTRL"
CTRL_BASE="$(git -C "$CTRL" rev-parse HEAD)"
sed -i 's/^updated: ".*"/updated: "2026-02-02"/' "$CTRL/registry/dependencies.yml"
printf '  beta: { name: Beta }\n' >> "$CTRL/registry/dependencies.yml"
sed -i '/^## Entries$/a\\n- **2026-02-02** — synthetic entry for the beta row' "$CTRL/registry/CHANGELOG.md"
CTRL_HEAD="$(commit_all "$CTRL" "edit both files, dates agree")"

workflow_expect "both files edited with agreeing dates passes" \
  "$GATE" 0 "$HOLDS" "$CTRL" pull_request "$CTRL_BASE" "$CTRL_HEAD"
workflow_expect "...and passes on the push arm" \
  "$GATE" 0 "$HOLDS" "$CTRL" push "$CTRL_BASE" "$CTRL_HEAD"

UNREL="$WORK/unrelated"; new_repo "$UNREL"
UNREL_BASE="$(git -C "$UNREL" rev-parse HEAD)"
echo "touched" >> "$UNREL/README.md"
UNREL_HEAD="$(commit_all "$UNREL" "touch nothing under registry/")"

workflow_expect "a change touching no registry file passes" \
  "$GATE" 0 "$HOLDS" "$UNREL" pull_request "$UNREL_BASE" "$UNREL_HEAD"

# --- The merge-base property (#375). -----------------------------------------------------------
#
# main moves AFTER the fork, and the commit it moves by edits registry/dependencies.yml. The PR's own
# commits touch no registry file. Diffing against the base-branch TIP would report dependencies.yml as
# changed by this PR and demand a changelog entry from an author who never touched the registry —
# #375 measured that false accusation on this exact file. Diffing against the merge-base does not.
FORK="$WORK/forked"; new_repo "$FORK"
FORK_POINT="$(git -C "$FORK" rev-parse HEAD)"
git -C "$FORK" checkout -q -b pr
echo "pr work" >> "$FORK/README.md"
FORK_HEAD="$(commit_all "$FORK" "PR work, no registry file touched")"
git -C "$FORK" checkout -q main
printf '  gamma: { name: Gamma }\n' >> "$FORK/registry/dependencies.yml"
sed -i '/^## Entries$/a\\n- **2026-01-01** — someone else edited the registry on main' "$FORK/registry/CHANGELOG.md"
FORK_TIP="$(commit_all "$FORK" "main moves on, editing the registry")"

workflow_expect "a registry edit landed on main AFTER the fork is not attributed to the PR" \
  "$GATE" 0 "$HOLDS" "$FORK" pull_request "$FORK_TIP" "$FORK_HEAD"
[ "$FORK_POINT" != "$FORK_TIP" ] || bad "workflow: the merge-base leg's base tip never moved (leg is vacuous)"

# --- Fail closed (#266): an unknown changed set is not an empty one. ---------------------------
ZERO="0000000000000000000000000000000000000000"
workflow_expect "an unresolvable range endpoint is refused, not treated as no changes" \
  "$GATE" 1 "cannot resolve" "$VIOL" push "$ZERO" "$VIOL_HEAD"
workflow_expect "an event with no changed-set rule is refused" \
  "$GATE" 1 "no changed-set rule for event" "$VIOL" schedule "$VIOL_BASE" "$VIOL_HEAD"

echo
echo "registry-changelog fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
