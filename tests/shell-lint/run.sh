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
EXTRACT="$REPO_ROOT/scripts/lib/extract-workflow-shell.py"
INSTALL="$REPO_ROOT/scripts/install-shellcheck.sh"

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

# ---- 6c. SC2251: A BARE `!` STATEMENT SKIPS ERREXIT (.github#2689). ------------------------------
#      Bash exempts a `!`-inverted command from `errexit`, so a bare `! cmd` statement in a
#      `set -euo pipefail` fixture computes the right answer and DISCARDS it — the same property as
#      the SIGPIPE family `scripts/check-pipefail-assertions.py` guards, by a different mechanism.
#      SC2251 lives at `info`, below this gate's `warning` floor, so `lint-shell.sh` reaches it with
#      a check-scoped `-i SC2251` pass rather than by lowering SEVERITY (which shell-lint.yml:186
#      forbids) or by adding an exclusion list.
#
#      THE NEGATIVE LEGS ARE THE POINT HERE, not the positive one. The whole reason this is a
#      linter enablement instead of a hand-written rule is DISCRIMINATION: a naive `^\s*!` regex
#      is 20% wrong on this repo's corpus, because `find` takes `!` as its own operand. If these
#      legs ever red, the enablement has started making the #238 false accusation and the remedy is
#      NOT to suppress it per-file.
SC2251_BAD='#!/usr/bin/env bash
set -euo pipefail
out="hello world"
! grep -q needle <<<"$out"
echo "$out"
'
# Every spelling .github#2689 requires to stay silent, in one file, under errexit.
SC2251_OK='#!/usr/bin/env bash
set -euo pipefail
out="hello world"
if ! grep -q needle <<<"$out"; then echo "absent"; fi
! grep -q needle <<<"$out" && echo "found"
! grep -q needle <<<"$out" || echo "absent"
while ! grep -q needle <<<"$out"; do break; done
[ ! -f /nonexistent ] && echo "nofile"
find . -maxdepth 1 ! -name "Options.fs" -print
echo "$out"
'
# The identical bare statement WITHOUT errexit: nothing is being skipped, so there is nothing to say.
SC2251_NO_ERREXIT='#!/usr/bin/env bash
set -uo pipefail
out="hello world"
! grep -q needle <<<"$out"
echo "$out"
'

d="$(newrepo sc2251-bad)"
printf '%s' "$SC2251_BAD" > "$d/bare.sh"; git -C "$d" add -A
run_gate "$d"
sc2251_named=0
case "$OUT" in *SC2251*) sc2251_named=1 ;; esac
[ "$RC" = 1 ] && [ "$sc2251_named" = 1 ] && grep -q ': note: This ! is not on a condition and skips errexit\.' <<<"$OUT" \
  && ok ".github#2689: a bare '! cmd' statement under errexit REDS the gate, named as SC2251" \
  || bad ".github#2689: a bare '! cmd' statement under errexit must red as SC2251" "rc=$RC
$OUT"

d="$(newrepo sc2251-ok)"
printf '%s' "$SC2251_OK" > "$d/guarded.sh"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] \
  && ok ".github#2689: 'if !', '! &&', '! ||', 'while !', '[ ! -f ]' and find's '!' OPERAND are silent" \
  || bad ".github#2689: the enablement is accusing a safe '!' spelling (#238)" "rc=$RC
$OUT"

d="$(newrepo sc2251-noerrexit)"
printf '%s' "$SC2251_NO_ERREXIT" > "$d/bare.sh"; git -C "$d" add -A
run_gate "$d"
[ "$RC" = 0 ] \
  && ok ".github#2689: the same bare statement WITHOUT errexit is not a finding" \
  || bad ".github#2689: SC2251 must be scoped to errexit" "rc=$RC
$OUT"

# ---- 7. LIVE-TREE AUTHORITY LIVES IN THE WORKFLOW'S `lint` JOB. -----------------------------------
# This fixture proves synthetic sensitivity and subject discovery. It deliberately does not invoke
# the checker over `$REPO_ROOT`: doing so duplicated the dedicated live job's complete pass. Leg 9g
# still reads one captured `--list` to prove the extensionless kit and workflow shell remain subjects.

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

# ...and THE EXTRACTOR ITSELF, invoked DIRECTLY — never through lint-shell.sh's wrapper. The wrapper's
# `if ! python3 ... ; then exit 2; fi` launders ANY nonzero exit into 2, including a crash, so Leg 9d
# above (and its composite-shell sibling) can only ever observe the wrapper's behaviour, never the
# extractor's own documented CLI contract. That gap is exactly how a `DiscoveryError` reference with no
# matching class definition shipped once already: every discovery failure died with `NameError` instead
# of the message this script was written to give, and the ONLY reason Leg 9d still read rc=2 is that a
# Python `NameError` is ALSO a nonzero exit the wrapper flattens to 2. Direct invocation is the only way
# to see the tool rather than the wrapper around it (.github#2493 review round 1).
wf_out="$WORK/wf-composite-noshell-direct-out"
mkdir -p "$wf_out"
direct_out="$(python3 "$EXTRACT" "$d" "$wf_out" 2>&1)"; direct_rc=$?
[ "$direct_rc" = 2 ] && ok "#2493: the extractor's OWN CLI exits 2 on a shell-less composite step (direct invocation)" \
  || bad "#2493: direct invocation must exit 2, not crash" "rc=$direct_rc
$direct_out"
printf '%s' "$direct_out" | grep -q 'no shell:' \
  && ok "#2493: ...and prints the DESIGNED diagnostic, not a NameError" \
  || bad "#2493: the extractor's own diagnostic message must actually be constructed and printed" "$direct_out"
printf '%s' "$direct_out" | grep -qi 'NameError\|Traceback' \
  && bad "#2493: the extractor must never crash with an uncaught NameError/Traceback on a step it is designed to refuse" "$direct_out" \
  || ok "#2493: ...and no NameError/Traceback leaked (DiscoveryError is a real, defined class)"

# ...and the SAME direct-invocation property for the OTHER DiscoveryError site: unparseable YAML.
d2="$(newrepo wf-badyaml-direct)"
mkdir -p "$d2/.github/workflows"
printf 'name: [unterminated\n' > "$d2/.github/workflows/bad.yml"
git -C "$d2" add -A
wf_out2="$WORK/wf-badyaml-direct-out"
mkdir -p "$wf_out2"
direct_out2="$(python3 "$EXTRACT" "$d2" "$wf_out2" 2>&1)"; direct_rc2=$?
[ "$direct_rc2" = 2 ] && ok "#2493: the extractor's OWN CLI exits 2 on unparseable workflow YAML (direct invocation)" \
  || bad "#2493: direct invocation must exit 2 on bad YAML, not crash" "rc=$direct_rc2
$direct_out2"
printf '%s' "$direct_out2" | grep -qi 'NameError\|Traceback' \
  && bad "#2493: unparseable YAML must not crash with an uncaught NameError/Traceback either" "$direct_out2" \
  || ok "#2493: ...and no NameError/Traceback leaked here either"

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

# ---- 9f2. THE SC2050 HANDLING IS OCCURRENCE-SCOPED, NOT BLANKET AND NOT MERELY LINE-SCOPED
#      (.github#2493 review rounds 1-2). Round 1 used a file-wide `-e SC2050` flag on the whole
#      workflow-embedded invocation, and the critic proved that wrong by mutating the SUBJECT: a step
#      with a GENUINE SC2050 typo and ZERO `${{ }}` involvement anywhere in it. A blanket exclusion
#      cannot tell that apart from the substitution artifact Leg 9f defends — it hides BOTH.
WF_TYPO_RUN='ref="$1"
if [ "$ref" = "main" ]; then
  echo "on main"
fi
if [ '"'"'GITHUB_REF_NAME'"'"' = '"'"'main'"'"' ]; then
  echo "this can never be true — GITHUB_REF_NAME is missing its $"
fi
'
d="$(newrepo wf-sc2050-real-typo)"
wf_workflow_with "$d" "$WF_TYPO_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] \
  && ok "#2493: a genuine SC2050 typo with NO \${{ }} involvement still REDS the gate" \
  || bad "#2493: a real SC2050 defect unrelated to GitHub-expression substitution must not be hidden" "rc=$RC
$OUT"
printf '%s' "$OUT" | grep -q ': warning: .*\[SC2050\]' \
  && ok "#2493: ...and the finding is specifically identified as SC2050" \
  || bad "#2493: the real typo's finding should be reported as SC2050" "$OUT"

# ---- 9f3. THE EXACT ROUND-2 ESCAPE: a resembling-but-different substitution shape sharing ONE PHYSICAL
#      LINE with a genuine, unrelated SC2050 defect. Round 2's fix moved the round-1 blanket exclusion to
#      a PER-LINE inline `# shellcheck disable=SC2050` pragma — narrower, but still line-scoped, and the
#      critic proved that ALSO wrong: `${{ }}` embedded with literal apostrophes inside a DOUBLE-quoted
#      string (`echo "...'${{ x }}'..."`, the exact shape behind the Leg 9f / SC2027 history) makes the
#      SAME textual pattern this repo's substitution produces appear on a line — and shellcheck's own
#      `disable` directive suppresses the WHOLE following physical line, not just the clause that
#      resembled the trigger. The genuine, unrelated `'GITHUB_REF_NAME' = 'main'` typo on the SECOND
#      clause of the line below was silently swallowed by that mechanism; it must not be swallowed here.
WF_COLLISION_RUN='echo "code '"'"'${{ steps.audit.outputs.rc }}'"'"'" && [ '"'"'GITHUB_REF_NAME'"'"' = '"'"'main'"'"' ]
'
d="$(newrepo wf-sc2050-collision)"
wf_workflow_with "$d" "$WF_COLLISION_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] \
  && ok "#2493: the round-2 same-line collision (resembling shape + genuine unrelated typo) still REDS the gate" \
  || bad "#2493: a genuine SC2050 defect sharing a line with a resembling-but-different substitution artifact must not be hidden" "rc=$RC
$OUT"
printf '%s' "$OUT" | grep -q 'SC2050' \
  && ok "#2493: ...and the finding is specifically identified as SC2050" \
  || bad "#2493: the collision leg's finding should be reported as SC2050" "$OUT"

# ---- 9f4. THE HARDER CASE: TWO GENUINE bracket/test CONSTRUCTS SHARING ONE LINE, only the FIRST one a
#      substitution site. Occurrence-level filtering (by shellcheck's own reported column against a
#      precisely-computed construct span — `scripts/lib/extract-workflow-shell.py`'s
#      `find_sc2050_protected_spans`) must suppress ONLY the first construct's finding and keep the
#      second, even though both are genuine `[ ... ]` tests on the identical physical line. A line-scoped
#      mechanism — pragma OR exclusion — cannot pass this leg by construction; only per-occurrence
#      filtering can.
WF_TWO_BRACKETS_RUN="[ '\${{ github.event_name }}' = 'workflow_dispatch' ] && [ 'GITHUB_REF_NAME' = 'main' ]
"
d="$(newrepo wf-sc2050-two-brackets)"
wf_workflow_with "$d" "$WF_TWO_BRACKETS_RUN"
git -C "$d" add -A
run_gate "$d"
[ "$RC" = 1 ] \
  && ok "#2493: two genuine [ ] constructs on one line — only the substitution-caused one is suppressed, the other REDS" \
  || bad "#2493: a second, genuine [ ] construct sharing a line with a real substitution site must not be hidden" "rc=$RC
$OUT"
printf '%s' "$OUT" | grep -q 'SC2050' \
  && ok "#2493: ...and the surviving finding is specifically identified as SC2050" \
  || bad "#2493: the surviving construct's finding should be reported as SC2050" "$OUT"

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

# ---- 10. scripts/install-shellcheck.sh: THE ACQUISITION PATH ITSELF (.github#2501). ------------------
#      Legs 1-9g above are entirely about scripts/lint-shell.sh — the LINTER. None of them ever put a
#      network failure in front of the workflow's OWN binary-acquisition step, because none of them
#      call install-shellcheck.sh at all. That gap is exactly how PR #2479 reddened on four `503`s from
#      a PR that touched no shell whatsoever (run 31632742170): the acquisition step had no test of its
#      own, hermetic or otherwise. These legs are that test, and they use `file://` URLs throughout —
#      never a real host, reachable or not — so "network source unavailable" is exercised exactly, with
#      no timeout, no flakiness of its own, and no dependence on this fixture's own network access.
#
#      A SYNTHETIC pin, deliberately NOT 0.11.0 / SHELLCHECK_SHA256 above: these legs test the
#      ACQUISITION MECHANISM (cache-hit skips network, checksum retained regardless of source, a total
#      failure is distinguishable from a lint finding), not today's real shellcheck release. Reusing
#      the real pin would make this fixture's own network-independence depend on that release still
#      existing, unrelated to what these legs are actually proving.
inst_ver="9.9.9-fixture"
inst_src="$WORK/install-src"
mkdir -p "$inst_src"
fake_bin_content='#!/usr/bin/env bash
echo "fake shellcheck v9.9.9-fixture"
'
mkdir -p "$WORK/install-tarroot/shellcheck-v${inst_ver}"
printf '%s' "$fake_bin_content" > "$WORK/install-tarroot/shellcheck-v${inst_ver}/shellcheck"
chmod +x "$WORK/install-tarroot/shellcheck-v${inst_ver}/shellcheck"
good_tar="$inst_src/shellcheck-v${inst_ver}.linux.x86_64.tar.xz"
tar -cJf "$good_tar" -C "$WORK/install-tarroot" "shellcheck-v${inst_ver}"
inst_sha="$(sha256sum "$good_tar" | awk '{print $1}')"

# A second tarball whose CONTENT differs (so its checksum genuinely differs from $inst_sha) — the
# "wrong artifact at a URL" case, distinct from "URL unreachable".
mkdir -p "$WORK/install-tarroot-bad/shellcheck-v${inst_ver}"
printf '%s' "$fake_bin_content"'# tampered
' > "$WORK/install-tarroot-bad/shellcheck-v${inst_ver}/shellcheck"
chmod +x "$WORK/install-tarroot-bad/shellcheck-v${inst_ver}/shellcheck"
bad_tar="$inst_src/wrong-content.tar.xz"
tar -cJf "$bad_tar" -C "$WORK/install-tarroot-bad" "shellcheck-v${inst_ver}"

run_install() {
  # run_install <cache-dir> <urls> -> sets IRC and IOUT ($3.. forwarded as extra positional args, unused)
  IOUT="$(SHELLCHECK_URLS="$2" bash "$INSTALL" "$inst_ver" "$inst_sha" "$1" 2>&1)"; IRC=$?
}

# ---- 10a. Cold cache, a reachable+correct source: acquires, extracts, and the binary actually runs. ---
d="$WORK/install-cold-ok"
run_install "$d" "file://$good_tar"
if [ "$IRC" = 0 ]; then
  bin_path="$(printf '%s' "$IOUT" | tail -1)"
  [ -x "$bin_path" ] && "$bin_path" 2>/dev/null | grep -q 'fake shellcheck v9.9.9-fixture' \
    && ok "#2501: cold cache + reachable, correct source acquires a working, checksum-verified binary" \
    || bad "#2501: the acquired binary must exist and run" "bin=$bin_path
$IOUT"
else
  bad "#2501: acquisition from a reachable, correct source must succeed (rc=0)" "rc=$IRC
$IOUT"
fi

# ---- 10b. WARM cache + an UNREACHABLE source: still succeeds, and never touches the source at all. ----
#      This is the property the workflow's `actions/cache` restore depends on: once ANY run has
#      acquired the tarball, every later run is zero-network, so a source outage cannot red the job.
#      THE CACHE STATE MODELED HERE IS `extracted/` INCLUDED, not the tarball alone (round-1 review
#      finding): `actions/cache` restores `$CACHE_DIR` whole, so a REAL warm production cache always
#      carries a prior run's `extracted/` alongside the tarball. A fixture that only ever pre-seeds the
#      tarball is simpler than production and cannot exercise the path that mattered — this leg's
#      `extracted/` is a genuine, correctly-extracted prior run, and Leg 10b2 right below is the one
#      that plants a MISMATCHED `extracted/` to prove that state is never trusted either.
d="$WORK/install-warm-unreachable"
mkdir -p "$d/extracted/shellcheck-v${inst_ver}"
cp "$good_tar" "$d/shellcheck-v${inst_ver}.linux.x86_64.tar.xz"
printf '%s' "$fake_bin_content" > "$d/extracted/shellcheck-v${inst_ver}/shellcheck"
chmod +x "$d/extracted/shellcheck-v${inst_ver}/shellcheck"
run_install "$d" "file://$WORK/install-src/does-not-exist.tar.xz"
[ "$IRC" = 0 ] \
  && ok "#2501: a WARM cache (tarball + a real prior extraction) acquires successfully with every source URL unreachable" \
  || bad "#2501: a pre-verified cache entry must not require the network at all" "rc=$IRC
$IOUT"

# ---- 10b2. THE ROUND-1 REVIEW FINDING, REPRODUCED EXACTLY: a checksum-valid tarball sits beside a
#      DIFFERENT, NON-MATCHING executable at the extracted `$BIN` path — the critic's own plant. An
#      earlier draft of install-shellcheck.sh skipped extraction whenever `$BIN` already existed, so
#      this state acquired with rc=0 AND printed the path to the UNVERIFIED planted binary, which the
#      caller then executed. `verify_checksum` only ever re-checks the TARBALL; nothing re-derived
#      `$BIN` from it unless extraction actually ran. This leg proves extraction now runs
#      UNCONDITIONALLY: acquisition still succeeds (rc=0, still zero-network — the tarball was already
#      verified), but the binary at the printed path is OVERWRITTEN with the genuine one from the
#      verified tarball, never the planted impostor.
d="$WORK/install-warm-tampered-bin"
mkdir -p "$d/extracted/shellcheck-v${inst_ver}"
cp "$good_tar" "$d/shellcheck-v${inst_ver}.linux.x86_64.tar.xz"
printf '#!/usr/bin/env bash\necho "PLANTED NON-MATCHING BINARY"\n' > "$d/extracted/shellcheck-v${inst_ver}/shellcheck"
chmod +x "$d/extracted/shellcheck-v${inst_ver}/shellcheck"
run_install "$d" "file://$WORK/install-src/does-not-exist.tar.xz"
if [ "$IRC" = 0 ]; then
  bin_path="$(printf '%s' "$IOUT" | tail -1)"
  ran="$("$bin_path" 2>/dev/null)"
  if [ "$ran" = "PLANTED NON-MATCHING BINARY" ]; then
    bad "#2501: round-1 finding REGRESSED — the planted, non-matching binary at \$BIN was executed unverified" "$IOUT"
  elif printf '%s' "$ran" | grep -q 'fake shellcheck v9.9.9-fixture'; then
    ok "#2501: a tampered/mismatched \$BIN is overwritten from the verified tarball before it can run — the plant never executes"
  else
    bad "#2501: the acquired binary's output matched neither the planted impostor nor the genuine one" "ran=$ran
$IOUT"
  fi
else
  bad "#2501: acquisition with a checksum-valid tarball must still succeed even when \$BIN was tampered" "rc=$IRC
$IOUT"
fi

# ---- 10c. COLD cache + an UNREACHABLE source: fails, and DISTINGUISHABLY — never a lint finding. ------
#      Acceptance criterion 1 on .github#2501: an acquisition failure must not be able to produce a
#      shell-lint verdict, and must be told apart from one. rc=2 (never 1, lint-shell.sh's finding
#      code) and the message names a transport failure, not "shellcheck reported" anything.
d="$WORK/install-cold-unreachable"
run_install "$d" "file://$WORK/install-src/does-not-exist.tar.xz"
[ "$IRC" = 2 ] \
  && ok "#2501: cold cache + every source unreachable fails with rc=2 (transport, not a finding)" \
  || bad "#2501: total acquisition failure must be rc=2" "rc=$IRC
$IOUT"
printf '%s' "$IOUT" | grep -q 'COULD NOT ACQUIRE' \
  && ok "#2501: ...and the message is explicitly a COULD NOT ACQUIRE, distinguishable from a finding" \
  || bad "#2501: the failure message must name itself as an acquisition failure" "$IOUT"
printf '%s' "$IOUT" | grep -qi 'shellcheck reported\|shell-lint: shellcheck' \
  && bad "#2501: an acquisition failure must never read as a shellcheck FINDING" "$IOUT" \
  || ok "#2501: ...and it never launders as a shellcheck finding"

# ---- 10d. Checksum mismatch FAILS CLOSED — a wrong-content source is refused, not silently accepted. --
#      Acceptance criterion 2: checksum verification is retained and a mismatch still fails closed.
#      This is the gate-inversion companion for the acquisition path itself: mutate the source content
#      away from the pin and acquisition must red, exactly the way an unreachable source reds above.
d="$WORK/install-checksum-mismatch"
run_install "$d" "file://$bad_tar"
[ "$IRC" = 2 ] \
  && ok "#2501: a checksum MISMATCH fails closed (rc=2) — the wrong artifact is refused, not installed" \
  || bad "#2501: a checksum mismatch must fail, never silently succeed" "rc=$IRC
$IOUT"
printf '%s' "$IOUT" | grep -qi 'checksum mismatch' \
  && ok "#2501: ...and says so explicitly (checksum mismatch), not a generic failure" \
  || bad "#2501: a checksum-mismatch failure should name itself" "$IOUT"
[ -x "$d/extracted/shellcheck-v${inst_ver}/shellcheck" ] \
  && bad "#2501: a checksum-mismatched artifact must never be extracted/installed" \
  || ok "#2501: ...and nothing was extracted from the unverified artifact"

# ---- 11. ONE ANALYSIS PER PROJECTION + DETERMINISTIC MANIFEST/RECEIPT (.github#3053). ------------
d="$(newrepo single-analysis)"
mkdir -p "$d/.github/workflows"
printf '%s' "$CLEAN_SHELL" >"$d/clean.sh"
printf '%s\n' 'name: fixture' 'on: push' 'jobs:' '  one:' '    runs-on: ubuntu-latest' \
  '    steps:' '      - run: echo clean' >"$d/.github/workflows/ci.yml"
git -C "$d" add -A

counting="$WORK/counting-shellcheck"
printf '%s\n' '#!/usr/bin/env bash' \
  'case " $* " in *" -f json1 "*) printf "%s\n" "$*" >>"$SHELLCHECK_COUNT_LOG" ;; esac' \
  'exec "$REAL_SHELLCHECK" "$@"' >"$counting"
chmod +x "$counting"
count_log="$WORK/shellcheck-invocations.log"
manifest="$WORK/shell-lint-manifest.json"
receipt="$WORK/shell-lint-receipt.json"
: >"$count_log"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK_COUNT_LOG="$count_log" \
  SHELLCHECK="$counting" SHELL_LINT_MANIFEST="$manifest" SHELL_LINT_RECEIPT="$receipt" \
  bash "$GATE" 2>&1)"; RC=$?
analysis_calls="$(wc -l <"$count_log" | tr -d ' ')"
file_calls="$(grep -c -- '-x -S info -f json1' "$count_log" || true)"
workflow_calls="$(grep -c -- '-S warning -f json1' "$count_log" || true)"
[ "$RC" = 0 ] && [ "$analysis_calls" = 2 ] && [ "$file_calls" = 1 ] && [ "$workflow_calls" = 1 ] \
  && ok ".github#3053: exactly one structured ShellCheck analysis runs for each non-empty projection" \
  || bad ".github#3053: file/workflow projections must each be analyzed once" "rc=$RC calls=$analysis_calls file=$file_calls workflow=$workflow_calls\n$OUT"

manifest_digest="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["digest"])' "$manifest")"
if python3 - "$manifest" "$receipt" <<'PY'
import json, sys
m = json.load(open(sys.argv[1], encoding="utf-8"))
r = json.load(open(sys.argv[2], encoding="utf-8"))
assert m["schema"] == "fsgg.shell-lint-manifest/v1"
assert [row["path"] for row in m["subjects"]["files"]] == ["clean.sh"]
assert len(m["subjects"]["workflowEmbedded"]) == 1
assert all(len(row["sha256"]) == 64 for rows in m["subjects"].values() for row in rows)
assert set(m["implementation"]) == {"extractor", "occurrenceFilter", "selector", "lint"}
assert r["schema"] == "fsgg.shell-lint-receipt/v1"
assert r["manifestDigest"] == m["digest"]
assert r["subjectCounts"] == {"files": 1, "workflowEmbedded": 1}
assert r["invocationCounts"] == {"files": 1, "workflowEmbedded": 1, "total": 2}
assert set(r["phaseDurationsMs"]) == {"discovery", "manifest", "fileShellcheck", "fileSelection", "workflowShellcheck", "workflowSelection", "total"}
assert r["verdict"] == {"exitCode": 0, "name": "clean"}
PY
then
  ok ".github#3053: manifest membership/config hashes and the critical-path receipt are complete"
else
  bad ".github#3053: manifest or receipt is incomplete" "$OUT"
fi

bad_receipt="$WORK/invalid-invocation-receipt.json"
durations='{"discovery":0,"manifest":0,"fileShellcheck":0,"fileSelection":0,"workflowShellcheck":0,"workflowSelection":0,"total":0}'
empty_manifest="$WORK/empty-subject-manifest.json"
python3 - "$manifest" "$empty_manifest" <<'PY'
import hashlib, json, sys
source, target = sys.argv[1:]
doc = json.load(open(source, encoding="utf-8"))
doc["subjects"] = {"files": [], "workflowEmbedded": []}
doc.pop("digest")
payload = json.dumps(doc, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
doc["digest"] = hashlib.sha256(payload).hexdigest()
open(target, "w", encoding="utf-8").write(json.dumps(doc, sort_keys=True, separators=(",", ":")) + "\n")
PY
for invalid_spec in \
  "$manifest|2|1|duplicate file analysis" \
  "$manifest|0|1|missing file analysis" \
  "$manifest|1|0|missing workflow analysis" \
  "$empty_manifest|1|0|analysis over an empty file projection"; do
  IFS='|' read -r invalid_manifest file_count workflow_count invalid_label <<<"$invalid_spec"
  python3 "$REPO_ROOT/scripts/lib/select-shellcheck-findings.py" receipt \
    --manifest "$invalid_manifest" --output "$bad_receipt" --durations "$durations" \
    --file-invocations "$file_count" --workflow-invocations "$workflow_count" \
    --exit-code 0 --verdict clean >"$WORK/invalid-invocation.out" 2>&1
  invalid_invocation_rc=$?
  [ "$invalid_invocation_rc" = 2 ] \
    && ok ".github#3053: receipt emission refuses $invalid_label" \
    || bad ".github#3053: invocation counts must match subject nonemptiness exactly ($invalid_label)" "rc=$invalid_invocation_rc\n$(cat "$WORK/invalid-invocation.out")"
done

: >"$count_log"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK_COUNT_LOG="$count_log" \
  SHELLCHECK="$counting" SHELL_LINT_MANIFEST="$manifest" SHELL_LINT_RECEIPT="$receipt" \
  bash "$GATE" 2>&1)"; RC=$?
stable_digest="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["digest"])' "$manifest")"
[ "$RC" = 0 ] && [ "$stable_digest" = "$manifest_digest" ] \
  && ok ".github#3053: unchanged subjects and policy reproduce the same canonical manifest digest" \
  || bad ".github#3053: unchanged input must keep the manifest digest stable" "before=$manifest_digest after=$stable_digest\n$OUT"

printf '\necho changed\n' >>"$d/clean.sh"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK_COUNT_LOG="$count_log" \
  SHELLCHECK="$counting" SHELL_LINT_MANIFEST="$manifest" SHELL_LINT_RECEIPT="$receipt" \
  bash "$GATE" 2>&1)"; RC=$?
content_digest="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["digest"])' "$manifest")"
[ "$RC" = 0 ] && [ "$content_digest" != "$stable_digest" ] \
  && ok ".github#3053: changing subject bytes invalidates the manifest digest" \
  || bad ".github#3053: content changes must invalidate the manifest" "before=$stable_digest after=$content_digest\n$OUT"

OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK_COUNT_LOG="$count_log" \
  SHELLCHECK="$counting" SEVERITY=error SHELL_LINT_MANIFEST="$manifest" SHELL_LINT_RECEIPT="$receipt" \
  bash "$GATE" 2>&1)"; RC=$?
config_digest="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["digest"])' "$manifest")"
[ "$RC" = 0 ] && [ "$config_digest" != "$content_digest" ] \
  && ok ".github#3053: changing severity policy invalidates the manifest digest" \
  || bad ".github#3053: config changes must invalidate the manifest" "before=$content_digest after=$config_digest\n$OUT"

malformed="$WORK/malformed-shellcheck"
printf '%s\n' '#!/usr/bin/env bash' \
  'case " $* " in *" --version "*) exec "$REAL_SHELLCHECK" "$@" ;; esac' \
  'printf "%s\n" '\''{"comments":[{"file":"incomplete"}]}'\''' \
  'exit 1' >"$malformed"
chmod +x "$malformed"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK="$malformed" bash "$GATE" 2>&1)"; RC=$?
[ "$RC" = 2 ] && grep -q 'structured-output boundary refused input' <<<"$OUT" \
  && ok ".github#3053: malformed or incomplete ShellCheck JSON is a no-verdict, never clean" \
  || bad ".github#3053: malformed structured output must fail closed" "rc=$RC\n$OUT"

malformed_message="$WORK/malformed-message-shellcheck"
printf '%s\n' '#!/usr/bin/env bash' \
  'case " $* " in *" --version "*) exec "$REAL_SHELLCHECK" "$@" ;; esac' \
  'printf "%s\n" '\''{"comments":[{"file":"bad.sh","line":1,"column":1,"level":"warning","code":2086,"message":["not","text"]}]}'\''' \
  'exit 1' >"$malformed_message"
chmod +x "$malformed_message"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK="$malformed_message" bash "$GATE" 2>&1)"; RC=$?
[ "$RC" = 2 ] && grep -q 'structured-output boundary refused input' <<<"$OUT" \
  && ok ".github#3053: a non-string diagnostic message is malformed, not a confident finding" \
  || bad ".github#3053: rendered ShellCheck fields must be type-checked" "rc=$RC\n$OUT"

malformed_level="$WORK/malformed-level-shellcheck"
printf '%s\n' '#!/usr/bin/env bash' \
  'case " $* " in *" --version "*) exec "$REAL_SHELLCHECK" "$@" ;; esac' \
  'printf "%s\n" '\''{"comments":[{"file":"bad.sh","line":1,"column":1,"level":["warning"],"code":2086,"message":"text"}]}'\''' \
  'exit 1' >"$malformed_level"
chmod +x "$malformed_level"
OUT="$(cd "$d" && env REAL_SHELLCHECK="$SHELLCHECK" SHELLCHECK="$malformed_level" bash "$GATE" 2>&1)"; RC=$?
[ "$RC" = 2 ] && grep -q 'structured-output boundary refused input' <<<"$OUT" \
  && ! grep -q 'Traceback' <<<"$OUT" \
  && ok ".github#3053: a non-string diagnostic level is malformed without a traceback" \
  || bad ".github#3053: level type validation must precede severity lookup" "rc=$RC\n$OUT"

echo
echo "shell-lint fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
