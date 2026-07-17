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

# ---- 7. THE REAL TREE. Without this, every leg above is synthetic. --------------------------------
run_gate "$REPO_ROOT"
[ "$RC" = 0 ] \
  && ok "the SHIPPED tree is clean at severity 'warning' ($(cd "$REPO_ROOT" && bash "$GATE" --list | wc -l | tr -d ' ') files)" \
  || bad "this repo's own shell must be clean" "rc=$RC
$OUT"

# ...and the real tree's subject includes the kit itself. A green that skipped fsgg-coord is #648
# re-entered: the item was filed BECAUSE the kit is the one file no linter reads.
(cd "$REPO_ROOT" && bash "$GATE" --list) | grep -qx 'scripts/fsgg-coord' \
  && ok "#648: the real subject includes scripts/fsgg-coord — the kit is linted in the repo that owns it" \
  || bad "#648: the kit must be in the subject" "$(cd "$REPO_ROOT" && bash "$GATE" --list)"

echo
echo "shell-lint fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
