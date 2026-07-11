# shellcheck shell=bash
# roots.sh — resolve a tree's agent-skill root set, shared across scripts/ (FS-GG/.github#525).
#
# A SOURCED fragment, not an executable: no shebang, no top-level effects. Source it AFTER defining
# `die`. Like lib/args.sh (#356), the guard body is shared and the per-script parts are not — here
# that means the caller supplies its own DEFAULT, because the two lanes legitimately differ:
#
#   skill-union-assert.sh  product lane — ADR-0011's three (.claude/.codex/.agents), fanned out by fsgg-sdd
#   coordination-sync      kit lane     — the two it materializes into (.claude/skills .agents/skills)
#
# It is the PRECEDENCE and the PARSING that must be shared, not the default. Before this hoist, only
# the asserter read the tree's `.agent-skill-roots` (#517) and the writer hardcoded its own set, so a
# tree's root set had two sources of truth that agreed only by coincidence of defaults (#525). The
# moment a tree declared anything else they diverged silently, and in the direction that matters:
# the writer would skip a declared root and the asserter would then blame the TREE for the omission.

# read_roots_decl <file> — parse a checked-in roots declaration: whitespace/newline-separated roots,
# `#` comments allowed. `\r` is stripped with the other separators, NOT left on the token: a CRLF
# checkout (Windows, or a .gitattributes `eol=crlf`) would otherwise yield a root '.claude/skills\r',
# whose directory does not exist — reporting "configured root is absent" for a root that is right
# there. That is #517's own failure mode one layer down, and it points at an existing root, so it
# reads even more like drift.
read_roots_decl() {
  sed 's/#.*$//' "$1" | tr '\n\t\r' '   ' | tr -s ' ' | sed 's/^ *//; s/ *$//'
}

# resolve_roots <tree> <default-roots> <default-label> [<explicit-roots>]
#
# Sets ROOTS, ROOTS_SRC (a human label naming which rule won), and ROOTS_FROM_DEFAULT (non-empty iff
# the default was used — callers hint at the declaration when a default-sourced root is missing).
#
# Precedence:  <explicit>  >  $AGENT_SKILL_ROOTS  >  <tree>/.agent-skill-roots  >  <default>
#
# <explicit> is the caller's own flag (`--roots`), already validated by need_val, so a non-empty
# value means "set" and an empty one means "not given" — `--roots ""` cannot reach here. A set-but-
# EMPTY $AGENT_SKILL_ROOTS likewise reads as unset and falls through, exactly as it did when each
# caller wrote this as one `${AGENT_SKILL_ROOTS:-<default>}` expansion.
#
# A declaration that parses to nothing (empty, or all comments) is a MISCONFIGURATION, not an empty
# root set: a tree that bothered to check the file in meant to say something. Falling through to the
# default there would silently substitute a root set the tree explicitly declined — the fail-open
# shape (#266) arriving through a config file that looks authoritative. So: die.
resolve_roots() {
  local tree="$1" default="$2" default_label="$3" explicit="${4:-}"
  local decl="$tree/.agent-skill-roots"

  ROOTS_FROM_DEFAULT=""
  if [ -n "$explicit" ]; then
    ROOTS="$explicit";                ROOTS_SRC="--roots"
  elif [ -n "${AGENT_SKILL_ROOTS:-}" ]; then
    ROOTS="$AGENT_SKILL_ROOTS";       ROOTS_SRC="\$AGENT_SKILL_ROOTS"
  elif [ -f "$decl" ]; then
    ROOTS="$(read_roots_decl "$decl")"
    ROOTS_SRC=".agent-skill-roots"
    [ -n "$ROOTS" ] || die "$decl declares no roots (it is empty, or all comments)."
  else
    ROOTS="$default";                 ROOTS_SRC="$default_label"
    ROOTS_FROM_DEFAULT=1
  fi
}
