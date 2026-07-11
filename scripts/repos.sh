#!/usr/bin/env bash
# repos.sh — validator + query helper for registry/repos.yml, the FS-GG org repo roster (ADR-0019).
#
# The roster is the ONE authoritative list of framework repos the org fabrics iterate, with a
# per-repo `receives` capability list. This script is how the fabrics READ it (so they stop
# hardcoding the list) and how CI VALIDATES it (schema + invariants + content-addressed kit).
# Deliberately shell + jq (YAML read via yq or python3+pyyaml) so .github stays self-contained and
# takes no dependency on the SDD-owned typed registry validator. Mirrors scripts/skill-union-assert.sh.
#
# Usage:
#   repos.sh validate [--registry <file>] [--root <dir>]   # schema + invariants + kit digests
#   repos.sh list (--receives <cap> | --all) [--field id|full] [--registry <file>]
#                                                           # roster query for consumers (apply-labels …).
#                                                           # --all: every rostered repo, receives or not.
#   repos.sh caps [--field id|workflow|receivers|reason] [--registry <file>]
#                                                           # the AUDITED capabilities (repos-audit.sh).
#                                                           # No --field: a TSV row per capability —
#                                                           # id, workflow, receivers, reason.
#   repos.sh kit [--field id|kind|source] [--kind skill|client] [--registry <file>]
#                                                           # the kit item list, in roster order
#   repos.sh relock [--registry <file>] [--root <dir>]      # REGENERATE registry/repos.lock from the
#                                                           # kit: sources. The lock is a GENERATED, CI-gated
#                                                           # artifact — never reserve it in a Paths: touch-set
#                                                           # (#309/#527); regenerate and note it as drift.
#   repos.sh digest <path>                                  # reference digest: skill dir -> sha256 of its
#                                                           # SKILL.md; file -> sha256 of the file
#   repos.sh -h | --help
#
# Exit: 0 = ok; 1 = a validation violation (each printed with ::error::); 2 = misconfiguration.

set -euo pipefail

ROOT_DEFAULT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REG_DEFAULT="$ROOT_DEFAULT/registry/repos.yml"

# The controlled set of capabilities a repo may `receive`. A `receives` value outside this set is a
# validation error — so a typo can't silently exclude a repo from a fabric. Grow it deliberately.
KNOWN_CAPS='["labels","coordination-kit","build-config","lockfile-sync","contract-coherence"]'

die() { echo "::error::repos-registry: $*" >&2; exit 2; }

# need_val <subcommand> <flag> [value …] — a missing flag value is a usage error ("I was called
# wrong"), never a finding about the roster. `${2:?…}` would let bash exit 1, the code this script
# reserves for "I checked, and it is invalid", leaving a caller unable to tell a typo'd command line
# from a broken registry. Route it through die() (exit 2, misconfiguration) instead.
#
# Two deviations from repos-audit.sh's need_val, both because THIS script has three subcommands where
# that one has a single top-level parser:
#   - it takes the subcommand, so the diagnostic names it. `--field` and `--registry` are shared by
#     `list`, `kit` and `validate`; an unprefixed message cannot be traced back to its call site, and
#     every other die() in these parsers already says `list:` / `kit:` / `validate:`.
#   - a `--flag`-looking value is a missing value, not a value. Without this, `list --receives
#     --registry r.yml` silently takes "--registry" as the capability and then blames `r.yml` for
#     being an unknown arg — naming an innocent token for a mistake made two arguments earlier.
need_val() {
  local sub="$1"; shift
  [ $# -ge 2 ] && [ -n "${2:-}" ] || die "$sub: $1 needs a value."
  case "$2" in --*) die "$sub: $1 needs a value (got flag '$2')." ;; esac
}

# Anchored on the `# Exit:` line, not a line count: adding a usage line must not spill the script's
# own code into --help (a fixed range did, printing `set -euo pipefail`).
usage() { sed -n '2,/^# Exit:/p' "$0" | sed 's/^# \{0,1\}//; s/^#$//'; }

command -v jq >/dev/null 2>&1 || die "jq not found (required)."

# YAML -> JSON on stdout. Prefer yq (present on GitHub runners), fall back to python3+pyyaml
# (the same fallback ladder scripts/sync-build-config.sh uses for XML validation).
yaml2json() {
  if command -v yq >/dev/null 2>&1; then
    yq -o=json '.' "$1"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys,yaml,json; json.dump(yaml.safe_load(open(sys.argv[1])), sys.stdout, default=str)' "$1"
  else
    die "need yq or python3+pyyaml to read YAML: $1"
  fi
}

# Reference digest generator (shared rule so the sync/coherence gate and the registry never drift):
# a skill directory digests its SKILL.md only (byte-equivalent to skill-union-assert's skill_digest);
# a plain file digests the file.
digest() {
  local path="$1"
  if [ -d "$path" ]; then
    [ -f "$path/SKILL.md" ] || die "no SKILL.md under skill dir: $path"
    sha256sum "$path/SKILL.md" | cut -d' ' -f1
  elif [ -f "$path" ]; then
    sha256sum "$path" | cut -d' ' -f1
  else
    die "source not found: $path"
  fi
}

# ---- the kit lock: the GENERATED half of the registry (#527) ------------------------------------
# The digests used to be a `sha256:` field on each `kit:` row in repos.yml. Because a kit source is
# content-addressed, editing ANY kit source obliged the worker to re-pin its hash — inside the
# AUTHORED roster. So every kit edit had to reserve `registry/repos.yml` in its `Paths:` touch-set and
# serialised against every other kit edit, and against anyone genuinely authoring a roster row.
#
# #309's rule (a generated, CI-gated artifact must not be reserved) could not reach that, because the
# rule classifies a FILE and the generated thing was a FIELD inside an authored one. So: split the
# field out into a file the generator owns outright. `repos.lock` is emitted by `relock`, guarded by
# repos-registry-selftest, and authored by NOBODY — a collision in it is a rebase, not a decision.
#
# Format is deliberately `sha256sum`-shaped (`<hash>  <source>`), sorted by source: the most boring,
# most diffable, most obviously-machine-written thing available. A hand-edit is meant to look wrong.
# The lock is named for its REGISTRY (repos.yml -> repos.lock), not for its directory: a `--registry`
# pointed at another roster must lock THAT roster, beside it, under its own name. Deriving the name
# from the directory would hand every registry in a directory one shared lock — fine until two of them
# disagree, and then it is silent cross-contamination in the one file whose whole job is to be a
# faithful function of its input.
lock_path() { printf '%s\n' "${1%.yml}.lock"; }

LOCK_HEADER='# registry/repos.lock — GENERATED. Do not edit; run `scripts/repos.sh relock`.
#
# The content-addressed digest of every `kit:` source in repos.yml, in `sha256sum` format.
#
# This is a GENERATED, CI-GATED ARTIFACT (FS-GG/.github#309): a checked-in generator emits it and
# `repos-registry-selftest` fails on any diff, so nobody AUTHORS it — a collision here is a REBASE,
# not a decision. DO NOT reserve it in a `Paths:` touch-set (#527); regenerate it and name it as
# expected drift in the PR.'

# Emit the lock for a registry to stdout. One line per `kit:` row, sorted by source for determinism.
gen_lock() {
  local reg="$1" root="$2" json src
  json="$(yaml2json "$reg")" || die "cannot parse YAML: $reg"
  printf '%s\n' "$LOCK_HEADER"
  while IFS= read -r src; do
    [ -n "$src" ] || continue
    printf '%s  %s\n' "$(digest "$root/$src")" "$src"
  done < <(echo "$json" | jq -r '(.kit // [])[] | .source' | LC_ALL=C sort)
}

# Read a lock into `<source>\t<sha256>` lines, comments and blanks dropped.
read_lock() {
  sed 's/#.*$//' "$1" | awk 'NF >= 2 { print $2 "\t" $1 }' | LC_ALL=C sort
}

cmd_relock() {
  local reg="$REG_DEFAULT" root=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --registry) need_val relock "$@"; reg="$2"; shift 2 ;;
      --root)     need_val relock "$@"; root="$2"; shift 2 ;;
      *)          die "relock: unknown arg '$1'." ;;
    esac
  done
  [ -f "$reg" ] || die "registry not found: $reg"
  [ -n "$root" ] || root="$(cd "$(dirname "$reg")/.." && pwd)"
  local lock; lock="$(lock_path "$reg")"
  gen_lock "$reg" "$root" > "$lock.tmp" && mv "$lock.tmp" "$lock"
  echo "repos-registry: relocked $(read_lock "$lock" | wc -l) kit digest(s) -> $lock"
}

cmd_list() {
  local cap="" all=0 field="full" reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --receives) need_val list "$@"; cap="$2";   shift 2 ;;
      --all)      all=1;                          shift 1 ;;
      --field)    need_val list "$@"; field="$2"; shift 2 ;;
      --registry) need_val list "$@"; reg="$2";   shift 2 ;;
      *)          die "list: unknown arg '$1'." ;;
    esac
  done
  # --all is how repos-audit.sh sweeps for an adopted-but-UNROSTERED capability: a repo that wires a
  # fabric it never declared is invisible to a `--receives` query by construction — the query trusts
  # the very declaration that is missing — so the reverse check has to start from every repo (#503).
  if [ "$all" = 1 ]; then
    [ -z "$cap" ] || die "list: --all and --receives are mutually exclusive."
  else
    [ -n "$cap" ] || die "list: --receives <cap> or --all is required."
  fi
  case "$field" in id|full) ;; *) die "list: --field must be id or full." ;; esac
  [ -f "$reg" ] || die "registry not found: $reg"
  if [ "$all" = 1 ]; then
    yaml2json "$reg" | jq -r --arg f "$field" '.repos[] | .[$f]'
  else
    yaml2json "$reg" | jq -r --arg cap "$cap" --arg f "$field" \
      '.repos[] | select((.receives // []) | index($cap)) | .[$f]'
  fi
}

# The AUDITED capabilities: those that map to a reusable .github workflow. repos-audit.sh reads its
# whole mandate from here — which capabilities exist, which workflow each one is wired by, and which
# ones claim to have no receiver at all. It used to hardcode that in a `wf_for_cap` case statement,
# so the roster and the audit could disagree about what the org even has, and did (#503).
#
# With no --field this emits a TSV row per capability — the audit needs every column at once, and
# four `--field` passes over the same file is the kind of thing that drifts out of step.
cmd_caps() {
  local field="" reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --field)    need_val caps "$@"; field="$2"; shift 2 ;;
      --registry) need_val caps "$@"; reg="$2";   shift 2 ;;
      *)          die "caps: unknown arg '$1'." ;;
    esac
  done
  case "$field" in ""|id|workflow|receivers|reason) ;;
    *) die "caps: --field must be id, workflow, receivers or reason." ;; esac
  [ -f "$reg" ] || die "registry not found: $reg"
  if [ -n "$field" ]; then
    yaml2json "$reg" | jq -r --arg f "$field" '(.capabilities // [])[] | (.[$f] // "")'
  else
    yaml2json "$reg" | jq -r \
      '(.capabilities // [])[] | [.id, (.workflow // ""), (.receivers // ""), (.reason // "")] | @tsv'
  fi
}

# The kit item list, in roster order. Consumers that name the kit's contents (the propagate PR
# title) read it from here rather than hardcoding — the roster is the one place the kit is defined,
# and a kit item added there must not need an edit in every fabric that mentions it.
cmd_kit() {
  local field="id" kind="" reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --field)    need_val kit "$@"; field="$2"; shift 2 ;;
      --kind)     need_val kit "$@"; kind="$2";  shift 2 ;;
      --registry) need_val kit "$@"; reg="$2";   shift 2 ;;
      *)          die "kit: unknown arg '$1'." ;;
    esac
  done
  case "$field" in id|kind|source) ;; *) die "kit: --field must be id, kind or source." ;; esac
  case "$kind" in ""|skill|client) ;; *) die "kit: --kind must be skill or client." ;; esac
  [ -f "$reg" ] || die "registry not found: $reg"
  yaml2json "$reg" | jq -r --arg f "$field" --arg k "$kind" \
    '(.kit // [])[] | select($k == "" or .kind == $k) | .[$f]'
}

cmd_validate() {
  local reg="$REG_DEFAULT" root="$ROOT_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --registry) need_val validate "$@"; reg="$2";  shift 2 ;;
      --root)     need_val validate "$@"; root="$2"; shift 2 ;;
      *)          die "validate: unknown arg '$1'." ;;
    esac
  done
  [ -f "$reg" ] || die "registry not found: $reg"
  local json; json="$(yaml2json "$reg")" || die "cannot parse YAML: $reg"
  echo "$json" | jq -e '.repos | type=="array" and length>0' >/dev/null \
    || die "repos[] is missing or empty."

  local fail=0
  err() { echo "::error::repos-registry: $*" >&2; fail=1; }

  # --- top-level shape ---
  echo "$json" | jq -e '.schemaVersion | type=="number"' >/dev/null || err "schemaVersion must be a number."
  echo "$json" | jq -e '(.updated // "") | test("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")' >/dev/null \
    || err "updated must be a YYYY-MM-DD date."
  local authority; authority="$(echo "$json" | jq -r '.authority // ""')"
  [ -n "$authority" ] || err "top-level 'authority' is required."

  # --- duplicate ids ---
  local dups; dups="$(echo "$json" | jq -r '[.repos[].id] | group_by(.)[] | select(length>1)[0]')"
  [ -z "$dups" ] || err "duplicate repo id(s): $(echo "$dups" | tr '\n' ' ')"

  # --- per-repo fields + receives vocabulary ---
  local id full role recv cap
  while IFS=$'\t' read -r id full role recv; do
    [ -n "$id" ] || continue
    [[ "$id"   =~ ^[a-z0-9.][a-z0-9._-]*$ ]] || err "repo id '$id' must be lowercase kebab/dotted."
    [[ "$full" =~ ^FS-GG/.+ ]]               || err "repo '$id' full '$full' must be 'FS-GG/<repo>'."
    case "$role" in authority|framework) ;; *) err "repo '$id' role '$role' invalid (authority|framework)." ;; esac
    for cap in $recv; do
      echo "$KNOWN_CAPS" | jq -e --arg c "$cap" 'index($c)' >/dev/null \
        || err "repo '$id' receives unknown capability '$cap' (known: $(echo "$KNOWN_CAPS" | jq -r 'join(", ")'))."
    done
  done < <(echo "$json" | jq -r '.repos[] | [.id, .full, .role, ((.receives // []) | join(" "))] | @tsv')

  # --- exactly one authority, matching the top-level, and not a kit receiver ---
  local authct; authct="$(echo "$json" | jq '[.repos[] | select(.role=="authority")] | length')"
  if [ "$authct" != "1" ]; then
    err "exactly one repo must have role 'authority' (found $authct)."
  else
    local authfull; authfull="$(echo "$json" | jq -r '.repos[] | select(.role=="authority") | .full')"
    [ "$authfull" = "$authority" ] || err "authority repo '$authfull' != top-level authority '$authority'."
  fi
  if echo "$json" | jq -e --arg a "$authority" \
      '.repos[] | select(.full==$a and ((.receives // []) | index("coordination-kit")))' >/dev/null; then
    err "authority repo '$authority' must not RECEIVE coordination-kit — it is the source."
  fi

  # --- outside-fabric: the reviewed opt-out list (.github#269) ---
  # An exemption is a standing licence for every fabric to ignore a repo, so it must be shaped and
  # justified here. Closure against the LIVE org (a row naming a repo that does not exist; a row
  # that also appears in repos[]) is asserted by scripts/check-roster-closure.py, which can reach
  # the API; this validator only enforces the shape it can see offline.
  echo "$json" | jq -e '(."outside-fabric" // []) | type=="array"' >/dev/null \
    || err "'outside-fabric' must be a list (use [] when empty)."
  local ofull oreason
  while IFS=$'\t' read -r ofull oreason; do
    [ -n "$ofull" ] || continue
    [[ "$ofull" =~ ^FS-GG/.+ ]] || err "outside-fabric '$ofull' must be 'FS-GG/<repo>'."
    [ -n "$oreason" ] || err "outside-fabric '$ofull' needs a 'reason' — an unexplained exemption is a mute button."
    echo "$json" | jq -e --arg f "$ofull" '[.repos[].full] | index($f)' >/dev/null \
      && err "outside-fabric '$ofull' is also rostered in repos[]; it cannot be both inside and outside the fabric."
  done < <(echo "$json" | jq -r '(."outside-fabric" // [])[] | [.full, (.reason // "")] | @tsv')
  local odups; odups="$(echo "$json" | jq -r '[(."outside-fabric" // [])[].full] | group_by(.)[] | select(length>1)[0]')"
  [ -z "$odups" ] || err "duplicate outside-fabric entr(ies): $(echo "$odups" | tr '\n' ' ')"

  # --- audited capabilities (.github#503) ---
  # The cap -> reusable-workflow map, and the place a capability may declare it has NO receiver.
  # Both are load-bearing for repos-audit.sh, and both fail OPEN if they are wrong in the quiet
  # direction: a capability naming a workflow that does not exist audits nothing and reports
  # nothing, which is the vacuous green this block exists to make impossible.
  echo "$json" | jq -e '(.capabilities // []) | type=="array"' >/dev/null \
    || err "'capabilities' must be a list (omit it, or use [])."
  local capdups; capdups="$(echo "$json" | jq -r '[(.capabilities // [])[].id] | group_by(.)[] | select(length>1)[0]')"
  [ -z "$capdups" ] || err "duplicate capability id(s): $(echo "$capdups" | tr '\n' ' ')"

  local cid cwf crecv creason
  while IFS=$'\t' read -r cid cwf crecv creason; do
    [ -n "$cid" ] || continue
    echo "$KNOWN_CAPS" | jq -e --arg c "$cid" 'index($c)' >/dev/null \
      || err "capability '$cid' is not in the receives vocabulary (known: $(echo "$KNOWN_CAPS" | jq -r 'join(", ")'))."
    if [ -z "$cwf" ]; then
      err "capability '$cid' has no 'workflow' — a capability is audited BY a reusable workflow; without one there is nothing to audit."
    elif [ ! -f "$root/.github/workflows/$cwf" ]; then
      # A typo'd filename is the fail-open case: every receiver is checked for a `uses:` of a
      # workflow that cannot exist, so every receiver "is not wired" — or, if the capability has no
      # receivers, nothing is checked at all and the audit is green about a workflow that is gone.
      err "capability '$cid' names workflow '$cwf', which is not in .github/workflows/."
    elif ! grep -qE '^[[:space:]]*workflow_call:' "$root/.github/workflows/$cwf"; then
      # `receives` means "this repo CALLS the authority's reusable workflow". A workflow without a
      # workflow_call trigger cannot be called, so no receiver could ever wire it and the audit
      # would report a gap against every one of them, forever.
      #
      # Anchored, so a COMMENT cannot satisfy it. An unanchored `grep -q workflow_call:` matches the
      # word anywhere in the file — including a line like `# this is not a workflow_call: trigger` —
      # which would pass a workflow that nothing can `uses:` as reusable. A check whose subject is
      # "can this really be called?" must not be satisfiable by prose about calling.
      err "capability '$cid' names workflow '$cwf', which has no 'workflow_call:' trigger — it is not reusable, so no repo can wire it."
    fi
    case "$crecv" in
      "") ;;   # the normal case: receivers are whichever repos[] declare the cap in `receives`
      none)
        # A reviewed claim, like outside-fabric — never a mute button. Two things keep it honest:
        # a reason is mandatory here, and repos-audit.sh still SCANS every repo for a real caller,
        # so the claim is falsifiable at audit time rather than merely asserted at review time.
        [ -n "$creason" ] \
          || err "capability '$cid' declares 'receivers: none' with no 'reason' — an unexplained exemption is a mute button."
        if echo "$json" | jq -e --arg c "$cid" '[.repos[] | select((.receives // []) | index($c))] | length > 0' >/dev/null; then
          err "capability '$cid' declares 'receivers: none', but repo(s) roster it: $(echo "$json" | jq -r --arg c "$cid" '[.repos[] | select((.receives // []) | index($c)) | .id] | join(", ")')."
        fi
        ;;
      *) err "capability '$cid' receivers '$crecv' is invalid (omit it, or set it to 'none')." ;;
    esac
  done < <(echo "$json" | jq -r '(.capabilities // [])[] | [.id, (.workflow // ""), (.receivers // ""), (.reason // "")] | @tsv')

  # --- kit rows must not collide at the receiver (.github#348) ---
  # Two kit rows that resolve to one destination make the fabric unsatisfiable: coordination-sync
  # writes both to the same path (last row wins), then --check fails forever and apply cannot repair
  # it — the registry is valid and the fabric cannot honour it. validate is the gate whose job is to
  # reject exactly that, so uniqueness is enforced here, at the source, not discovered downstream.
  local kdups; kdups="$(echo "$json" | jq -r '[(.kit // [])[].id] | group_by(.)[] | select(length>1)[0]')"
  [ -z "$kdups" ] || err "duplicate kit id(s): $(echo "$kdups" | tr '\n' ' ')"
  # A skill materializes to <root>/<basename source>/SKILL.md, so its DESTINATION is a function of the
  # source basename, not the id — two skill rows with distinct ids but a shared basename still target
  # one path. (Clients name their own destination, so this is scoped to skills.)
  local bcol
  while IFS= read -r bcol; do
    [ -n "$bcol" ] || continue
    err "kit skill rows share destination basename — they materialize to one path: $bcol"
  done < <(echo "$json" | jq -r '
    [ (.kit // [])[] | select(.kind=="skill" and (.source | type=="string"))
      | { id, base: (.source | sub("/+$";"") | split("/") | last) } ]
    | group_by(.base)[] | select(length>1)
    | "\(.[0].base) <- \([.[].id] | join(", "))"')

  # --- the kit rows themselves: authored, and carrying NO digest (#527) ---
  local kid kind ksrc ksha
  while IFS=$'\t' read -r kid kind ksrc ksha; do
    [ -n "$kid" ] || continue
    # Same rule as repo ids. `repos.sh kit` feeds these straight into the propagate PR's title, so a
    # stray quote or control character in an id would surface there — validate at the source.
    [[ "$kid" =~ ^[a-z0-9.][a-z0-9._-]*$ ]] || err "kit id '$kid' must be lowercase kebab/dotted."
    case "$kind" in skill|client) ;; *) err "kit '$kid' kind '$kind' invalid (skill|client)." ;; esac
    [ -e "$root/$ksrc" ] || err "kit '$kid' source missing: $ksrc"
    # A digest back in the roster is SPLIT TRUTH, and the split is the whole bug (#527): two places
    # to state one fact, and the stale one is authoritative to whoever reads it first. Reject the
    # field outright rather than tolerate-and-ignore it — a tolerated field gets hand-edited, and a
    # hand-edited digest that nothing checks is exactly the silent staleness this move is avoiding.
    [ -z "$ksha" ] || err "kit '$kid' carries a 'sha256:' field — digests live in repos.lock (#527). Remove it and run: repos.sh relock"
  done < <(echo "$json" | jq -r '(.kit // [])[] | [.id, .kind, .source, (.sha256 // "")] | @tsv')

  # --- content-addressed kit: repos.lock is exactly the digests of the declared sources ---
  # The lock must be a REGENERATION of the roster, not merely consistent with it: a source the lock
  # omits is an unguarded kit item (fail-open, #266 — the receiver's bytes would drift with nothing
  # to say so), and a row the lock carries for a source no longer in the roster is a stale pin that
  # outlived its row. Comparing the whole generated file against the whole checked-in one catches
  # both, plus ordering and formatting drift, in one diff — and it is the same comparison the gate
  # makes, so `relock` is always the fix.
  local lock; lock="$(lock_path "$reg")"
  if [ ! -f "$lock" ]; then
    err "kit lock missing: $lock (generate it: repos.sh relock)"
  else
    local want got_lock
    want="$(gen_lock "$reg" "$root")" || err "cannot generate the kit lock (a source is missing or undigestable)."
    got_lock="$(cat "$lock")"
    if [ "$want" != "$got_lock" ]; then
      err "kit lock is STALE — repos.lock does not match the digests of the declared sources. Regenerate: repos.sh relock"
      diff <(printf '%s\n' "$got_lock") <(printf '%s\n' "$want") 2>/dev/null \
        | sed 's/^/::error::repos-registry:   /' >&2 || true
    fi
  fi

  if [ "$fail" -ne 0 ]; then
    echo "::error::repos-registry: INVALID — see errors above." >&2
    exit 1
  fi
  echo "repos-registry: OK — $(echo "$json" | jq '.repos | length') repo(s), $(echo "$json" | jq '(.capabilities // []) | length') audited capabilit(ies), $(echo "$json" | jq '(.kit // []) | length') kit item(s), authority=$authority"
}

case "${1:-}" in
  validate) shift; cmd_validate "$@" ;;
  relock)   shift; cmd_relock "$@" ;;
  list)     shift; cmd_list "$@" ;;
  caps)     shift; cmd_caps "$@" ;;
  kit)      shift; cmd_kit "$@" ;;
  digest)   shift; [ $# -ge 1 ] || die "digest: <path> required."; digest "$1" ;;
  -h|--help|help|"") usage ;;
  *)        die "unknown command '${1:-}' (try: repos.sh --help)." ;;
esac
