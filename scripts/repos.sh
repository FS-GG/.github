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
#   repos.sh caps [--field id|workflow|script|push|receivers|reason] [--registry <file>]
#                                                           # the AUDITED capabilities (repos-audit.sh).
#                                                           # No --field: a TSV row per capability —
#                                                           # id, workflow, script, push, receivers, reason.
#   repos.sh received [--registry <file>]                   # cap<TAB>the repos that receive it, for
#                                                           # EVERY capability the roster claims —
#                                                           # including one with no `capabilities:` row.
#                                                           # The audit's closure check reads this (#628).
#   repos.sh kit [--field id|kind|source|dest] [--kind skill|client|config] [--registry <file>]
#                                                           # the kit item list, in roster order
#   repos.sh relock [--registry <file>] [--root <dir>]      # REGENERATE registry/repos.lock from the
#                                                           # kit: sources. The lock is a GENERATED, CI-gated
#                                                           # artifact — never reserve it in a Paths: touch-set
#                                                           # (#309/#527); regenerate and note it as drift.
#   repos.sh relock --list                                  # kind<TAB>path<TAB>marker for what relock EMITS,
#                                                           # writing nothing (ADR-0044). Empty marker = the
#                                                           # whole file is generated, so verify-paths may
#                                                           # subtract it. The generator answers what it
#                                                           # generates; nothing downstream keeps a copy.
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

# `<path>` relative to `<root>`, for the `--list` contract (ADR-0044). Not `realpath
# --relative-to`: that is GNU-only, and this script is the one every fabric runs. A prefix strip is
# enough because both sides are already absolute and `lock_path` never escapes the root.
rel_to() {
  local p="$1" r="${2%/}"
  case "$p" in "$r"/*) printf '%s\n' "${p#"$r"/}" ;; *) printf '%s\n' "$p" ;; esac
}

cmd_relock() {
  local reg="$REG_DEFAULT" root="" list=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --registry) need_val relock "$@"; reg="$2"; shift 2 ;;
      --root)     need_val relock "$@"; root="$2"; shift 2 ;;
      --list)     list=1; shift 1 ;;
      *)          die "relock: unknown arg '$1'." ;;
    esac
  done
  [ -f "$reg" ] || die "registry not found: $reg"
  [ -n "$root" ] || root="$(cd "$(dirname "$reg")/.." && pwd)"
  local lock; lock="$(lock_path "$reg")"

  # ADR-0044: the generator enumerates its own output, so nothing downstream keeps a second copy of
  # it. The path is DERIVED — `lock_path` off the registry, exactly as the write below derives it —
  # rather than a literal, because a literal here would be the very hand-kept copy this answers.
  #
  # The EMPTY marker field is the load-bearing part: it says the WHOLE FILE is generated, so nobody
  # authors it and `verify-paths` may subtract it. A region generator fills that field in, and its
  # file stays declarable. Emitted BEFORE any write, and it writes nothing itself: `--list` is the
  # one question a caller must be able to ask a generator without it touching the tree.
  if [ "$list" = 1 ]; then
    printf 'kit-lock\t%s\t\n' "$(rel_to "$lock" "$root")"
    return 0
  fi

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

# The AUDITED capabilities. repos-audit.sh reads its whole mandate from here — which capabilities
# exist, HOW each one is detectable in a receiver, and which ones claim to have no receiver at all. It
# used to hardcode that in a `wf_for_cap` case statement, so the roster and the audit could disagree
# about what the org even has, and did (#503).
#
# A row declares EXACTLY ONE DETECTOR — the answer to "how would I know, by looking at the receiver,
# that it really participates?" (#628):
#
#   workflow: <f>.yml   PULL, by reusable workflow. The receiver calls FS-GG/.github's `<f>.yml`.
#                       Detected by a `uses:` of the authority's copy.
#   script:   <f>.sh    PULL, by script. The capability is delivered as a script, and the receiver
#                       wires it by INLINING a job that checks .github out and runs it. There is no
#                       reusable workflow to `uses:`, so the workflow detector cannot see it at all —
#                       which is how `build-config` came to be received by four repos and audited by
#                       nothing. Detected by a reference to the authority's script.
#   push:     true      PUSH. The AUTHORITY writes this INTO the receiver (apply-labels.sh reads this
#                       very roster and pushes the labels in). The receiver wires nothing, so there is
#                       no receiver-side artifact to detect and the `receives:` row is the INPUT to the
#                       push rather than a falsifiable claim about the receiver's config. Requires a
#                       `reason:` — this is a reviewed claim like `receivers: none`, never a blank.
#
# The point of naming the PUSH case out loud is that it is the ONLY honest way to be unauditable, and
# it has to be SAID. Before #628 a capability could simply have no row here, and then it was swept in
# neither direction while still being a legal `receives:` word — an absence that reads exactly like a
# licence, and was used as one (#626 concluded the shared tool manifest "propagates to nobody" from
# `build-config`'s empty rows, and shipped on it; four repos went red within twenty minutes).
#
# With no --field this emits a TSV row per capability — the audit needs every column at once, and six
# `--field` passes over the same file is the kind of thing that drifts out of step.
# What the roster actually CLAIMS: `cap<TAB>the repos that receive it`, one row per capability any
# repo declares — INCLUDING one the `capabilities:` block has no row for. That inclusion is the whole
# point (#628). Every other query here is keyed on the capabilities block, so a capability that is
# received but undeclared is invisible to all of them — which is exactly how `build-config` stayed
# invisible while four repos received it. This is the query that can SEE the gap, so it is the one the
# audit's closure check reads.
cmd_received() {
  local reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --registry) need_val received "$@"; reg="$2"; shift 2 ;;
      *)          die "received: unknown arg '$1'." ;;
    esac
  done
  [ -f "$reg" ] || die "registry not found: $reg"
  yaml2json "$reg" | jq -r '
    [ .repos[] | . as $r | (.receives // [])[] | { cap: ., repo: $r.id } ]
    | group_by(.cap)[]
    | [ .[0].cap, ([.[].repo] | join(", ")) ] | @tsv'
}

cmd_caps() {
  local field="" reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --field)    need_val caps "$@"; field="$2"; shift 2 ;;
      --registry) need_val caps "$@"; reg="$2";   shift 2 ;;
      *)          die "caps: unknown arg '$1'." ;;
    esac
  done
  case "$field" in ""|id|workflow|script|push|receivers|reason) ;;
    *) die "caps: --field must be id, workflow, script, push, receivers or reason." ;; esac
  [ -f "$reg" ] || die "registry not found: $reg"
  # `push` is a BOOLEAN in YAML, so `// ""` cannot blank it — `false // ""` is `false`, not "".
  # Normalize it to the string the shell readers compare against ("true" or empty), or a `push: false`
  # row would reach repos-audit.sh as the four characters "false" and be read as a live push detector.
  if [ -n "$field" ]; then
    yaml2json "$reg" | jq -r --arg f "$field" \
      '(.capabilities // [])[] | (if $f == "push" then (if .push == true then "true" else "" end) else (.[$f] // "") end)'
  else
    yaml2json "$reg" | jq -r \
      '(.capabilities // [])[]
       | [ .id, (.workflow // ""), (.script // ""),
           (if .push == true then "true" else "" end),
           (.receivers // ""), (.reason // "") ] | @tsv'
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
  case "$field" in id|kind|source|dest) ;; *) die "kit: --field must be id, kind, source or dest." ;; esac
  case "$kind" in ""|skill|client|config) ;; *) die "kit: --kind must be skill, client or config." ;; esac
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

  # TAB IS IFS-*WHITESPACE* IN BASH, so `IFS=$'\t' read` collapses runs of tabs and DROPS empty
  # fields — a row with an empty `workflow` would shift `script` left into it, and this loop would
  # then report "capability 'build-config' names workflow 'sync-build-config.sh'". The old 4-column
  # form never had an empty MIDDLE field, so it worked by luck; a 6-column row with three optional
  # detectors has one on almost every line. Re-delimit with a unit separator, which is NOT IFS
  # whitespace and therefore preserves empties. (jq's @tsv escapes any literal tab in a value, so the
  # substitution cannot corrupt a `reason:`.)
  local cid cwf cscript cpush crecv creason line
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    IFS=$'\x1f' read -r cid cwf cscript cpush crecv creason <<< "${line//$'\t'/$'\x1f'}"
    [ -n "$cid" ] || continue
    echo "$KNOWN_CAPS" | jq -e --arg c "$cid" 'index($c)' >/dev/null \
      || err "capability '$cid' is not in the receives vocabulary (known: $(echo "$KNOWN_CAPS" | jq -r 'join(", ")'))."

    # EXACTLY ONE DETECTOR (#628). Zero is the defect this rule exists to kill: a capability with no
    # detector is swept in NEITHER direction, so it cannot be found unwired and cannot be found
    # adopted-but-unrostered — it is simply outside the audit, silently, while remaining a legal
    # `receives:` word. Two is ambiguous: repos-audit would have to pick one, and a receiver that
    # satisfies the loose one would mask a gap in the strict one.
    local ndet=0
    [ -n "$cwf" ]     && ndet=$((ndet + 1))
    [ -n "$cscript" ] && ndet=$((ndet + 1))
    [ "$cpush" = true ] && ndet=$((ndet + 1))
    if [ "$ndet" -eq 0 ]; then
      err "capability '$cid' declares no detector — set 'workflow:' (a reusable workflow the receiver calls), 'script:' (an authority script the receiver runs from an inlined job), or 'push: true' with a reason (the authority writes it INTO the receiver; nothing is detectable receiver-side). A capability with no detector is audited in NEITHER direction while still being a legal 'receives' word, which is exactly how build-config came to be received by four repos and checked by nothing (#628)."
    elif [ "$ndet" -gt 1 ]; then
      err "capability '$cid' declares more than one detector (workflow/script/push) — a capability is verified ONE way; two lets a receiver satisfy the loose one and mask a gap in the strict one."
    fi

    if [ -n "$cwf" ]; then
      if [ ! -f "$root/.github/workflows/$cwf" ]; then
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
    fi

    # The script detector's subject must EXIST, for the same reason the workflow's must: the audit
    # greps receivers for a reference to `scripts/<f>`, so a typo'd or deleted script means every
    # receiver "is not wired" — a gate that is confidently wrong about every repo at once.
    if [ -n "$cscript" ]; then
      case "$cscript" in
        */*) err "capability '$cid' script '$cscript' must be a BARE filename, not a path — receivers check .github out under a directory of their own choosing (governance uses '_org-build/'), so only the basename is stable across them." ;;
      esac
      [ -f "$root/scripts/$cscript" ] \
        || err "capability '$cid' names script '$cscript', which is not in scripts/ — the audit greps receivers for a reference to it, so a script that does not exist reports every receiver unwired."
    fi

    if [ "$cpush" = true ]; then
      [ -n "$creason" ] \
        || err "capability '$cid' declares 'push: true' with no 'reason' — a capability that is not verifiable at the receiver is the one place this roster can be unfalsifiable, so it must be a reviewed claim, not a blank."
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
  done < <(echo "$json" | jq -r '(.capabilities // [])[] | [.id, (.workflow // ""), (.script // ""), (if .push == true then "true" else "" end), (.receivers // ""), (.reason // "")] | @tsv')

  # --- CLOSURE: a capability a repo RECEIVES must be one the audit can detect (#628) ---------------
  #
  # THE DEFECT, stated as an invariant: `{caps any repo receives} ⊆ {caps with a detector row}`.
  #
  # `build-config` was a legal `receives:` word (it is in KNOWN_CAPS) with NO `capabilities:` row, so
  # four repos declared it, four repos really enforced it, and repos-audit swept it in neither
  # direction for as long as it existed. The registry header meanwhile promised, in its own words,
  # that "this list can no longer rot without a red check" — a guarantee that was true for three of
  # five capabilities, and the next reader trusted it for all five. #626 did exactly that: it read the
  # empty `receives` rows as "propagates to nobody", shipped on it, and reddened four repos in twenty
  # minutes. An unaudited registry row is not a neutral gap; it is a false negative that reads like a
  # licence.
  #
  # Keyed on the ROSTER, not on KNOWN_CAPS: a vocabulary word nobody says is harmless (nothing claims
  # it, so nothing can rot), whereas a word a repo DOES say and nothing checks is the whole bug.
  local undetectable
  undetectable="$(echo "$json" | jq -r '
    ( [ (.capabilities // [])[].id ] ) as $detected
    | [ .repos[] | . as $r | (.receives // [])[] | select(. as $c | $detected | index($c) | not)
        | "\(.) (received by \($r.id))" ]
    | unique | join("; ")')"
  [ -z "$undetectable" ] \
    || err "capability/ies received but NOT detectable — no 'capabilities:' row, so repos-audit sweeps them in NEITHER direction and the roster's claim about them can never go red: $undetectable. Give each a detector row (workflow:/script:/push:)."

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
  # Re-delimit with a unit separator, exactly as the capabilities loop above does and for the same
  # reason: `dest` is empty on skill/client rows, so a plain `IFS=$'\t' read` — tab IS IFS-whitespace —
  # would collapse the empty field and shift a config row's dest into `sha256`, mis-reporting a valid
  # manifest as carrying a forbidden digest. `\x1f` is not IFS-whitespace and preserves empties.
  local kid kind ksrc ksha kdest line
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    IFS=$'\x1f' read -r kid kind ksrc ksha kdest <<< "${line//$'\t'/$'\x1f'}"
    [ -n "$kid" ] || continue
    # Same rule as repo ids. `repos.sh kit` feeds these straight into the propagate PR's title, so a
    # stray quote or control character in an id would surface there — validate at the source.
    [[ "$kid" =~ ^[a-z0-9.][a-z0-9._-]*$ ]] || err "kit id '$kid' must be lowercase kebab/dotted."
    case "$kind" in skill|client|config) ;; *) err "kit '$kid' kind '$kind' invalid (skill|client|config)." ;; esac
    [ -e "$root/$ksrc" ] || err "kit '$kid' source missing: $ksrc"
    # A digest back in the roster is SPLIT TRUTH, and the split is the whole bug (#527): two places
    # to state one fact, and the stale one is authoritative to whoever reads it first. Reject the
    # field outright rather than tolerate-and-ignore it — a tolerated field gets hand-edited, and a
    # hand-edited digest that nothing checks is exactly the silent staleness this move is avoiding.
    [ -z "$ksha" ] || err "kit '$kid' carries a 'sha256:' field — digests live in repos.lock (#527). Remove it and run: repos.sh relock"
    # A `config` row is a plain file delivered to a path that is NOT its source (a client's dest IS its
    # source; a skill's is derived from its basename), so it must NAME its receiver-relative dest — and
    # that dest may not be absolute or escape the receiver root. Any OTHER kind carrying a `dest` is a
    # confusion: its destination is already determined, so a stray `dest` would silently do nothing.
    if [ "$kind" = config ]; then
      [ -n "$kdest" ] || err "kit '$kid' is kind 'config' but declares no 'dest' — a config row must name the receiver-relative path it is delivered to."
      case "$kdest" in
        /*)   err "kit '$kid' dest '$kdest' must be receiver-RELATIVE, not absolute." ;;
        *..*) err "kit '$kid' dest '$kdest' must not escape the receiver root (no '..')." ;;
      esac
    else
      [ -z "$kdest" ] || err "kit '$kid' (kind '$kind') declares a 'dest' — only 'config' rows may; a skill's dest is derived from its basename and a client's IS its source path."
    fi
  done < <(echo "$json" | jq -r '(.kit // [])[] | [.id, .kind, .source, (.sha256 // ""), (.dest // "")] | @tsv')

  # --- non-skill kit rows must not collide at one receiver path (#348, extended for config) ---
  # A client lands at its source path, a config at its dest; two that resolve to one path make the
  # fabric unsatisfiable — the same defect the skill-basename check above rejects, one kind over.
  local ddups; ddups="$(echo "$json" | jq -r '
    [ (.kit // [])[] | if .kind=="client" then .source elif .kind=="config" then .dest else empty end ]
    | group_by(.)[] | select(length>1)[0]')"
  [ -z "$ddups" ] || err "kit client/config rows share a receiver destination: $ddups"

  # --- THE #1077 INVARIANT: the shim and its engine manifest ride the kit TOGETHER ---
  # `scripts/fsgg-coord` execs `fs.gg.coord.cli`, so a receiver that gets the shim but not the
  # `.config/dotnet-tools.json` that restores the engine has a tool it CANNOT run. That was the live
  # state: the shim rode coordination-kit (6 receivers), the manifest rode build-config (4), and
  # templates/audio fell in the gap — invisible to every gate because none asked "can this receiver run
  # the engine?" (#1077, epic #266). Both are kit rows now, so the two receiver sets are EQUAL by
  # construction; this asserts they cannot silently drift apart again — if the kit delivers the shim, it
  # must deliver the manifest. Keyed on the delivered PATHS, not row ids, so a rename cannot slip past.
  local shimmanifest; shimmanifest="$(echo "$json" | jq -r '
    ( [ (.kit // [])[] | if .kind=="client" then .source elif .kind=="config" then .dest else empty end ] ) as $d
    | if ($d | index("scripts/fsgg-coord")) and (($d | index(".config/dotnet-tools.json")) | not)
      then "MISSING" else "" end')"
  [ "$shimmanifest" != "MISSING" ] \
    || err "the coordination kit delivers the fsgg-coord shim (scripts/fsgg-coord) but NOT the engine manifest (.config/dotnet-tools.json): a receiver would get a tool it cannot run (#1077). Add a 'kind: config' kit row whose dest is '.config/dotnet-tools.json', or the shim and its manifest have drifted onto different fabrics again."

  # --- content-addressed kit: repos.lock is exactly the digests of the declared sources ---
  # The lock must be a REGENERATION of the roster, not merely consistent with it: a source the lock
  # omits is an unguarded kit item (fail-open, #266 — the receiver's bytes would drift with nothing
  # to say so), and a row the lock carries for a source no longer in the roster is a stale pin that
  # outlived its row. Comparing the whole generated file against the whole checked-in one catches
  # both, plus ordering and formatting drift, in one diff — and it is the same comparison the gate
  # makes, so `relock` is always the fix.
  local lock nkit; lock="$(lock_path "$reg")"
  nkit="$(echo "$json" | jq '(.kit // []) | length')"
  # A roster that ships no kit has nothing to lock, and demanding a lock for it would fail rosters
  # that legitimately have no `kit:` block (tests/roster-closure builds several). That is NOT a hole:
  # if a lock EXISTS it is always compared, so deleting the `kit:` block from a roster that has one
  # does not silence the check — it turns every checked-in pin into an orphan and the whole-file
  # comparison below reports it. "No kit" only excuses the lock when there is genuinely no kit.
  if [ "$nkit" -gt 0 ] && [ ! -f "$lock" ]; then
    err "kit lock missing: $lock (generate it: repos.sh relock)"
  elif [ -f "$lock" ]; then
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
  received) shift; cmd_received "$@" ;;
  kit)      shift; cmd_kit "$@" ;;
  digest)   shift; [ $# -ge 1 ] || die "digest: <path> required."; digest "$1" ;;
  -h|--help|help|"") usage ;;
  *)        die "unknown command '${1:-}' (try: repos.sh --help)." ;;
esac
