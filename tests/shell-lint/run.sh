#!/usr/bin/env bash
# Fixture for scripts/lint-shell.sh — the repo that AUTHORS the org's shell finally reads it (#648).
#
# THE FAILURE LEGS ARE THE POINT. A gate that cannot say NO is not a gate, and this one's specific way
# of failing open is DISCOVERY: it decides its own subject. A lint that silently examines nothing
# reports the same green as a lint over a clean tree, which is epic #266's signature exactly. So the
# legs below are mostly about what the gate FINDS, not about what shellcheck says.
#
# Leg 3 is the one that matters, and it is a real hole this gate had while it was being written:
# `scripts/fsgg-coord` — the kit, and the file #648 was FILED about — has no `.sh` extension, because
# it is a command. An extension-only sweep finds 47 of this repo's 51 shell files, skips the four
# spelled as commands, and reports green having never opened the one file the item names. That is not
# hypothetical; it is what the first draft of the discovery did.
#
# Leg 7 runs the gate against THIS REPO's real tree and requires green. Without it every leg above is
# synthetic, and the gate could pass its own fixture while the shipped shell rots.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/lint-shell.sh"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/shell-lint-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "shell-lint fixture — work='$WORK'"

# The gate needs A shellcheck. If the developer has none, say so and exit 2 — "I could not check" is
# not "it passed", which is the very distinction this fixture exists to defend.
SHELLCHECK="${SHELLCHECK:-shellcheck}"
command -v "$SHELLCHECK" >/dev/null 2>&1 || {
  echo "::error::shellcheck not found: '$SHELLCHECK'. Set SHELLCHECK=<path> (CI passes the pinned one)."
  exit 2
}
export SHELLCHECK

# A throwaway git repo: the gate enumerates with `git ls-files`, so an un-added file is invisible to
# it by design (an untracked scratch file is not this repo's shell).
newrepo() {
  local d="$WORK/$1"
  mkdir -p "$d" && git -C "$d" init -q 2>/dev/null
  git -C "$d" config user.email f@x && git -C "$d" config user.name f
  printf '%s' "$d"
}
# run_gate <dir> -> sets $RC and $OUT
run_gate() {
  OUT="$(cd "$1" && bash "$GATE" 2>&1)"; RC=$?
}

# A finding every shellcheck ≥0.11 reports at `warning`: an unquoted expansion that word-splits.
BAD_SHELL='#!/usr/bin/env bash
d=$1
rm -rf "$d"/*
cd $HOME/x
'
CLEAN_SHELL='#!/usr/bin/env bash
d="$1"
echo "$d"
'

# ---- 1. A clean tree passes. ---------------------------------------------------------------------
d="$(newrepo clean)"
printf '%s' "$CLEAN_SHELL" > "$d/ok.sh"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "a clean .sh tree passes (rc=0)" || bad "clean tree must pass" "rc=$RC
$OUT"

# ---- 2. THE GATE CAN SAY NO. ---------------------------------------------------------------------
d="$(newrepo dirty)"
printf '%s' "$BAD_SHELL" > "$d/bad.sh"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] && ok "a finding in a .sh file REDS the gate (rc=1)" || bad "the gate must red on a finding" "rc=$RC
$OUT"

# ---- 3. THE DISCOVERY HOLE: a command-named shell file, no extension. -----------------------------
#      This is `scripts/fsgg-coord`'s exact shape, and the exact shape of the hole an extension-only
#      sweep leaves. If this leg ever goes green with rc=0, the gate has stopped reading the kit.
d="$(newrepo extensionless)"
printf '%s' "$BAD_SHELL" > "$d/fsgg-coord-like"; chmod +x "$d/fsgg-coord-like"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] \
  && ok "#648: an EXTENSIONLESS bash-shebang file is discovered and linted (the fsgg-coord shape)" \
  || bad "#648: an extensionless shell file must not be skipped — this is the kit" "rc=$RC
$OUT"

# ...and it is genuinely the shebang doing that work, not the exec bit or the name.
d="$(newrepo shebang-only)"
printf '%s' "$BAD_SHELL" > "$d/somecommand"; git -C "$d" add -A   # NOT chmod +x
run_gate "$d"
[ "$RC" = 1 ] && ok "#648: ...discovered by its SHEBANG, with no exec bit and no extension" \
  || bad "#648: discovery must key on the shebang" "rc=$RC
$OUT"

# ---- 4. NO FALSE ACCUSATIONS: a non-shell shebang is not shell. -----------------------------------
#      A linter that reports bash findings against a python script is the #238 false accusation, and a
#      lint nobody can satisfy is a lint somebody deletes.
d="$(newrepo python)"
printf '#!/usr/bin/env python3\nimport os\nd = os.environ["HOME"]\nprint(f"cd {d}/x")\n' > "$d/tool.py"
printf '#!/usr/bin/env python3\nprint("no extension either")\n' > "$d/pytool"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 3 ] \
  && ok "#648: python files are NOT linted as shell — and a tree with no shell is rc=3, not a pass" \
  || bad "#648: a python tree must not be read as shell" "rc=$RC
$OUT"

# ...and the interpreter must be a whole PATH COMPONENT, not a suffix. This is a REGRESSION: the
# first draft's `(/[^[:space:]]*)*(ba|da|k)?sh` ate `/bin/z` and matched `sh` on the tail, so every
# one of these was "shell" — and shellcheck cannot read a word of any of them. Linting them emits
# nonsense against a correct file whose author cannot satisfy it (#238). If this leg ever reds, the
# gate has started accusing files it does not speak for.
for interp in /bin/zsh /bin/fish /bin/csh /bin/tcsh; do
  d="$(newrepo "notshell-$(basename "$interp")")"
  printf '#!%s\n%s' "$interp" "$BAD_SHELL" > "$d/script"; chmod +x "$d/script"; git -C "$d" add -A
  run_gate "$d"
  [ "$RC" = 3 ] \
    && ok "#648: '#!$interp' is NOT read as shell — its name merely ENDS in 'sh'" \
    || bad "#648: $interp must not be linted as shell (shellcheck cannot read it)" "rc=$RC
$OUT"
done

# ---- 5. ZERO SUBJECTS IS NOT GREEN (#266). --------------------------------------------------------
#      The failure this whole gate is aimed at: examining nothing and reporting success.
d="$(newrepo empty)"
printf 'hello\n' > "$d/README.md"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 3 ] && ok "#266: a tree with ZERO shell exits 3 (audited nothing) — never 0" \
  || bad "#266: zero discovered files must not report green" "rc=$RC
$OUT"
printf '%s' "$OUT" | grep -q 'discovered ZERO' \
  && ok "#266: ...and it SAYS the discovery is what broke, not the tree" \
  || bad "#266: rc=3 must name discovery as the fault" "$OUT"

# ---- 6. "I COULD NOT CHECK" IS NOT "IT PASSED". ---------------------------------------------------
d="$(newrepo noshellcheck)"
printf '%s' "$BAD_SHELL" > "$d/bad.sh"; git -C "$d" add -A
OUT="$(cd "$d" && SHELLCHECK=/nonexistent/shellcheck bash "$GATE" 2>&1)"; RC=$?
[ "$RC" = 2 ] && ok "#266: a missing shellcheck is rc=2 (could not run) — not 0, and not a finding" \
  || bad "#266: a missing shellcheck must exit 2" "rc=$RC
$OUT"

# ---- 6b. FOLLOWING IS EVIDENCE, NOT AN INFO-LEVEL FINDING (#1719). -------------------------------
d="$(newrepo sc1091)"; mkdir -p "$d/scripts/lib"
printf '#!/usr/bin/env bash\n# shellcheck source-path=SCRIPTDIR\nsource lib/ok.sh\n' > "$d/scripts/uses.sh"
printf '#!/usr/bin/env bash\nok() { :; }\n' > "$d/scripts/lib/ok.sh"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "#1719: a real source-path pragma follows its source and greens" \
  || bad "#1719: source-path control must start green" "rc=$RC\n$OUT"
sed -i '/^# shellcheck source-path=SCRIPTDIR$/d' "$d/scripts/uses.sh"
run_gate "$d"
[ "$RC" = 4 ] && printf '%s' "$OUT" | grep -q 'source-path=SCRIPTDIR' \
  && ok "#1719: an unfollowed source is rc=4 with its source-path remedy at warning floor" \
  || bad "#1719: SC1091 must red independently of SEVERITY=warning" "rc=$RC\n$OUT"
sed -i '2i# shellcheck source-path=SCRIPTDIR' "$d/scripts/uses.sh"
run_gate "$d"
[ "$RC" = 0 ] && ok "#1719: restoring the pragma greens the same checkout again" \
  || bad "#1719: restored source-path must green" "rc=$RC\n$OUT"

# ---- 7. THE REAL TREE. Without this, every leg above is synthetic. --------------------------------
run_gate "$REPO_ROOT"
[ "$RC" = 0 ] \
  && ok "the SHIPPED tree is clean at severity 'warning' ($(cd "$REPO_ROOT" && bash "$GATE" --list | wc -l | tr -d ' ') files)" \
  || bad "this repo's own shell must be clean" "rc=$RC
$OUT"

# ...and the real tree's subject includes the kit itself: see Leg 9g below, which reuses ONE captured
# `--list` for this assertion AND the workflow-embedded one rather than paying for a second ~15s
# extraction pass over the real tree.

# ---- 8. THE KIT'S SOURCES MUST BE FOLLOWABLE FROM ANY CWD (.github#1718). -------------------------
#      `scripts/skill-view` is a kit-materialized file: registry/repos.yml ships it, and its two
#      `lib/` rows, to all seven `coordination-kit` receivers. It `source`s both at startup through
#      RELATIVE `# shellcheck source=...` pragmas, which resolve against the LINTER's working
#      directory unless the file also carries a file-scoped `# shellcheck source-path=SCRIPTDIR`.
#      Without that line, `shellcheck -x scripts/skill-view` from the repo root lints it with BOTH
#      libraries unread and emits SC1091 — which is what reddened FS.GG.Game's 0.15.0 kit bump
#      (Game#514, run 30321396512), in a file no receiver is allowed to repair in its own tree.
#
#      A RECEIVER FOUND THIS, NOT US, and the reason is structural: `lint-shell.sh` runs at
#      `-S warning`, and SC1091 is an `info`, so the gate that owns this repo's shell is blind to it
#      by construction. Raising that floor is .github#1719 and is deliberately NOT done here — this
#      leg buys the ONE property #1718 paid for, at `-S info`, without moving any gate's severity.
#
#      TWO CWDs, because "resolves from any working directory" is the whole property. A pragma placed
#      next to the `source` lines instead of at file scope binds to the next command only and clears
#      exactly one of the two findings, so a single-cwd smoke test would not notice the difference.
KIT_SHELL="scripts/skill-view"
sc1091() { (cd "$1" && "$SHELLCHECK" -x -S info -f gcc "$2" 2>&1 | grep -c 'SC1091' || true); }

for cwd_label in "$REPO_ROOT:$KIT_SHELL" "/:$REPO_ROOT/$KIT_SHELL"; do
  cwd="${cwd_label%%:*}"; target="${cwd_label#*:}"
  n="$(sc1091 "$cwd" "$target")"
  [ "$n" = 0 ] \
    && ok "#1718: $KIT_SHELL has no SC1091 with cwd='$cwd' (its sources are followable)" \
    || bad "#1718: $KIT_SHELL emitted $n SC1091 finding(s) from cwd='$cwd' — the file-scoped '# shellcheck source-path=SCRIPTDIR' is missing or has been moved below the first command. A receiver's shell gate reds on this and CANNOT fix it locally: the file is kit-materialized." \
       "$(cd "$cwd" && "$SHELLCHECK" -x -S info -f gcc "$target" 2>&1)"
done

# ...and THE LEG CAN SAY NO. Strip the directive from a copy and the findings must come back — both
# of them. Without this, leg 8 above is a green that would also be green if shellcheck had stopped
# reporting SC1091 altogether, which is epic #266 one level up.
d="$WORK/sc1091-control"
mkdir -p "$d/scripts/lib"
grep -v '^# shellcheck source-path=SCRIPTDIR$' "$REPO_ROOT/$KIT_SHELL" > "$d/$KIT_SHELL"
cp "$REPO_ROOT"/scripts/lib/args.sh "$REPO_ROOT"/scripts/lib/roots.sh "$d/scripts/lib/"
n="$(sc1091 "$d" "$KIT_SHELL")"
[ "$n" = 2 ] \
  && ok "#1718: ...and REMOVING the directive brings both SC1091 findings back (control: $n)" \
  || bad "#1718: the control must reproduce exactly 2 SC1091 findings without the directive, got $n — this leg is no longer measuring what it claims" \
     "$(cd "$d" && "$SHELLCHECK" -x -S info -f gcc "$KIT_SHELL" 2>&1)"

# ---- 9. WORKFLOW-EMBEDDED SHELL (.github#2493). ----------------------------------------------------
#      `.github#2489` claimed "CI runs the pinned shellcheck" for shell that lived inside a `run:`
#      block in `.github/workflows/kit-auto-publish.yml` — and nothing had ever opened it. Legs 1-8
#      above are entirely about FILE-shaped discovery; none of them would catch that gap coming back,
#      because none of them ever puts a `run:` block in front of the gate. These legs are the ones that
#      would have caught #2489's inaccurate claim BEFORE a critic had to find it by hand.
WF_CLEAN_RUN='d="$1"
echo "$d"
'
# The same word-splitting hazard as $BAD_SHELL above, minus its shebang line (a `run:` block is never
# its own shebanged file).
WF_BAD_RUN='d=$1
rm -rf "$d"/*
cd $HOME/x
'

wf_workflow_with() {
  # wf_workflow_with <dir> <run-text> [shell]  — one job, one step, `runs-on: ubuntu-latest`.
  local d="$1" run="$2" shell="${3:-}"
  mkdir -p "$d/.github/workflows"
  {
    echo "name: ci"
    echo "on: push"
    echo "jobs:"
    echo "  build:"
    echo "    runs-on: ubuntu-latest"
    echo "    steps:"
    echo "      - name: embedded step"
    if [ -n "$shell" ]; then
      echo "        shell: $shell"
    fi
    echo "        run: |"
    printf '%s\n' "$run" | sed 's/^/          /'
  } > "$d/.github/workflows/ci.yml"
}

# ---- 9a. A clean embedded run: step passes. ---------------------------------------------------------
d="$(newrepo wf-clean)"
wf_workflow_with "$d" "$WF_CLEAN_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "#2493: a clean workflow-embedded run: step passes (rc=0)" \
  || bad "#2493: a clean embedded run: step must pass" "rc=$RC
$OUT"

# ---- 9b. THE GATE CAN SAY NO on embedded shell too. --------------------------------------------------
d="$(newrepo wf-dirty)"
wf_workflow_with "$d" "$WF_BAD_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] && ok "#2493: a finding in an embedded run: step REDS the gate (rc=1)" \
  || bad "#2493: the gate must red on a finding embedded in a workflow" "rc=$RC
$OUT"

# ---- 9c. A composite action's run: step, admitted by its explicit shell: bash. -----------------------
d="$(newrepo wf-composite-clean)"
mkdir -p "$d/.github/actions/thing"
{
  echo "name: thing"
  echo "runs:"
  echo "  using: composite"
  echo "  steps:"
  echo "    - name: embedded step"
  echo "      shell: bash"
  echo "      run: |"
  printf '%s\n' "$WF_CLEAN_RUN" | sed 's/^/        /'
} > "$d/.github/actions/thing/action.yml"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "#2493: a clean composite-action run: step (shell: bash) passes (rc=0)" \
  || bad "#2493: a clean composite run: step must pass" "rc=$RC
$OUT"

d="$(newrepo wf-composite-dirty)"
mkdir -p "$d/.github/actions/thing"
{
  echo "name: thing"
  echo "runs:"
  echo "  using: composite"
  echo "  steps:"
  echo "    - name: embedded step"
  echo "      shell: bash"
  echo "      run: |"
  printf '%s\n' "$WF_BAD_RUN" | sed 's/^/        /'
} > "$d/.github/actions/thing/action.yml"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] && ok "#2493: a finding in a composite-action run: step REDS the gate (rc=1)" \
  || bad "#2493: the gate must red on a finding in a composite action" "rc=$RC
$OUT"

# ---- 9d. A composite run: step with NO shell: is invalid workflow YAML, not a shell to guess at. -----
#      GitHub never defaults a composite step's shell (unlike a normal job step, which defaults to
#      bash on the runners this repo uses). Guessing bash here would silently lint — or silently skip
#      — a step whose actual interpreter this script does not actually know; #266's "I could not check
#      is not I checked and it's clean" applies to the EXTRACTOR's own subject, not only shellcheck's.
d="$(newrepo wf-composite-noshell)"
mkdir -p "$d/.github/actions/thing"
{
  echo "name: thing"
  echo "runs:"
  echo "  using: composite"
  echo "  steps:"
  echo "    - name: embedded step"
  echo "      run: |"
  printf '%s\n' "$WF_CLEAN_RUN" | sed 's/^/        /'
} > "$d/.github/actions/thing/action.yml"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 2 ] && ok "#2493: a composite run: step with no shell: is a discovery failure (rc=2), not a guess" \
  || bad "#2493: a shell-less composite step must not be silently guessed at" "rc=$RC
$OUT"

# ---- 9e. NO FALSE ACCUSATIONS: a shell shellcheck cannot read is never linted, embedded or not. ------
#      Same #238 property as Leg 4, one layer up: a `pwsh`/`python`/etc. `shell:` is real GitHub Actions
#      syntax this repo simply cannot check with shellcheck. The step body below is objectively bad
#      BASH — if it were ever mistakenly admitted, this leg's clean `.sh` file would not save it and RC
#      would be 1. Admitting it correctly means the tree is clean overall (RC=0): one real bash file,
#      one ignored pwsh step.
d="$(newrepo wf-notshell)"
printf '%s' "$CLEAN_SHELL" > "$d/ok.sh"
wf_workflow_with "$d" "$WF_BAD_RUN" "pwsh"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "#2493: a 'shell: pwsh' step is NOT read as shell — its content is never opened" \
  || bad "#2493: a non-bash shell: step must not be linted (shellcheck cannot read it)" "rc=$RC
$OUT"

# ---- 9f. GITHUB EXPRESSIONS (`\${{ }}`) do not themselves manufacture findings. -----------------------
#      Two shapes this extractor's substitution got wrong on the FIRST pass, over the real tree, before
#      either fix landed (.github#2493's own PR notes carry both shellcheck transcripts):
#        - `"...\${{ x }}[bot]"` (unbraced) read as unquoted array-subscript syntax -> spurious SC1087.
#        - `'\${{ x }}'` as the WHOLE content of a single-quoted comparison operand, exactly the common
#          `[ '\${{ github.event_name }}' = 'workflow_dispatch' ]` shape, read as two adjacent literal
#          strings -> spurious SC2050 ("did you forget the $ on a variable?"). It is not a typo: the
#          left side is genuinely dynamic, just substituted by GitHub before any shell parses it.
#      Both shapes appear below in one step; if either self-inflicted finding ever comes back, this leg
#      reds on a synthetic tree that has no real defect in it at all.
WF_EXPR_RUN='git config user.name "${{ steps.app-token.outputs.app-slug }}[bot]"
if [ '"'"'${{ github.event_name }}'"'"' = '"'"'workflow_dispatch'"'"' ]; then
  echo "manual"
fi
'
d="$(newrepo wf-gha-expr)"
wf_workflow_with "$d" "$WF_EXPR_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] && ok "#2493: \${{ }} expressions (bare and single-quoted-whole-token) do not self-accuse" \
  || bad "#2493: substituting GitHub expressions must not manufacture its own findings" "rc=$RC
$OUT"

# ---- 9g. THE REAL TREE'S WORKFLOW-EMBEDDED SUBJECT IS NON-VACUOUS. ------------------------------------
#      Leg 7 above already requires the real tree clean INCLUDING its workflow-embedded shell (the
#      extraction runs unconditionally, not behind a flag) — but a green over an empty subject is
#      exactly #266's failure shape one layer up. Reusing ONE `--list` capture for both this assertion
#      and the pre-existing #648 one keeps this leg from paying for a second full extraction pass over
#      the real tree (each is a real, non-trivial cost: ~15s of YAML parsing across ~100 workflows).
# A HERE-STRING, not a live pipe into `grep -q`: bash implements `<<<` via a temp file, so an early
# `-q` match can never SIGPIPE a still-writing producer. A live `(...) | grep -qx PATTERN` pipe hit
# exactly that race the moment `--list` grew past one pipe buffer (.github#2493's own PR notes carry
# the transcript) — `trap '' PIPE` in the gate itself is the belt, this is the suspenders for the one
# spot in this fixture that reads a large capture rather than a small one.
real_list="$(cd "$REPO_ROOT" && bash "$GATE" --list)"
grep -qx 'scripts/fsgg-coord' <<<"$real_list" \
  && ok "#648: the real subject includes scripts/fsgg-coord — the kit is linted in the repo that owns it" \
  || bad "#648: the kit must be in the subject" "$real_list"
grep -q '^workflow-embedded: \.github/workflows/kit-auto-publish\.yml:' <<<"$real_list" \
  && ok "#2493: the real subject includes workflow-embedded shell from kit-auto-publish.yml" \
  || bad "#2493: the real subject must include at least one real workflow's embedded shell — a green here with an empty workflow-embedded subject is discovery having broken, not a clean tree" "$real_list"

echo
echo "shell-lint fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
