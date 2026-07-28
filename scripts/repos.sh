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
#   repos.sh list (--receives <cap> | --all) [--field id|full] [--kit-delivery byte-copy|package] [--registry <file>]
#                                                           # --kit-delivery narrows a --receives
#                                                           # coordination-kit query to how the repo
#                                                           # takes the kit (absent row == byte-copy).
#                                                           # roster query for consumers (apply-labels …).
#                                                           # --all: every rostered repo, receives or not.
#   repos.sh caps [--field id|workflow|script|materializer|caller|push|receivers|reason] [--registry <file>]
#                                                           # the AUDITED capabilities (repos-audit.sh).
#                                                           # No --field: a TSV row per capability —
#                                                           # id, workflow, script, materializer, caller,
#                                                           # push, receivers, reason.
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
#   repos.sh digest <path>                                  # RAW content digest: skill dir -> sha256 of its
#                                                           # SKILL.md's bytes; file -> sha256 of the file.
#                                                           # NOT Fsgg.SkillMirror.sha256 — deliberately (see
#                                                           # the `digest()` header, .github#1585)
#
#   repos.sh require-context   --context <ctx> --receives <cap> [--branch <b>] [--only <full>]
#                              [--apply] [--registry <file>]
#                                                           # ADD <ctx> to the CLASSIC required status
#                                                           # checks of every repo the roster says
#                                                           # receives <cap>. Dry-run unless --apply.
#                                                           # ADD-ONLY: it cannot remove anything.
#   repos.sh unrequire-context --context <ctx> --receives <cap> --confirm-remove <ctx>
#                              [--branch <b>] [--only <full>] [--apply] [--registry <file>]
#                                                           # REMOVE <ctx>. A SEPARATE verb with a
#                                                           # SEPARATE confirmation that must repeat
#                                                           # the exact context name. Never reachable
#                                                           # from require-context (#1613).
#   repos.sh -h | --help
#
# Exit: 0 = ok; 1 = a validation violation (each printed with ::error::); 2 = misconfiguration.

set -euo pipefail

ROOT_DEFAULT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REG_DEFAULT="$ROOT_DEFAULT/registry/repos.yml"

# The controlled set of capabilities a repo may `receive`. A `receives` value outside this set is a
# validation error — so a typo can't silently exclude a repo from a fabric. Grow it deliberately.
KNOWN_CAPS='["labels","coordination-kit","build-config","lockfile-sync","contract-coherence","skill-union"]'

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

# RAW CONTENT DIGEST — the shared rule so the sync/coherence gate and the registry never drift: a skill
# directory digests its SKILL.md only; a plain file digests the file. Bytes in, sha256 out, no
# normalization of any kind.
#
# THIS IS NOT `Fsgg.SkillMirror.sha256`, AND THAT IS A DECISION, NOT AN OVERSIGHT (.github#1585).
# This header used to say "byte-equivalent to skill-union-assert's `skill_digest`". That sentence was
# true when it was written, was falsified by .github#1547 — which folded CRLF into both VERIFIERS to
# follow the library — and was left standing because it sat outside #1547's touch-set. It is removed
# rather than repaired, because repairing it would mean folding here, and folding here would be wrong:
#
#   1. WHAT THIS DIGEST IS FOR IS BYTE INTEGRITY, NOT BODY IDENTITY. It writes `registry/repos.lock`
#      and, through `src/FS.GG.Kit/stage-kit.sh` (which calls this very command), the `sha256` column
#      of the packed `kit-manifest.tsv`. A receiver checks a MATERIALIZED FILE against that column at
#      restore. A normalizing digest would report a CRLF-mangled delivery as intact — it would answer
#      "the same body" to a question that asked "the same bytes".
#   2. IT WOULD BREAK A LIVE CONTAINMENT CHECK. `scripts/check-kit-published-coherence.py` requires
#      every `repos.lock` digest to appear among the canonical `kit-manifest.tsv` digests. Those are
#      per-file RAW digests taken by the plain-file arm below. Folding only the skill-dir arm would
#      make a lock row for a CRLF SKILL.md a value no manifest row carries — a gate failure invented
#      by the fix.
#   3. IT IS NOT ONLY A SKILL DIGEST. The `kit:` roster's `client` and `config` rows
#      (`scripts/fsgg-coord`, `dist/dotnet/.config/dotnet-tools.json`) come through the plain-file arm.
#      "Canonical body" is not a defined notion for a shell script or a tool manifest.
#
# So the two producers .github#1585 found split APART here: `scripts/generate-driver-manifest` adopted
# the library's normalization, because the `sha256` it emits is re-derived by both verifiers; this one
# stays raw, because nothing re-derives it as a canonical body and two consumers depend on it being the
# bytes. The difference is PINNED, not promised: tests/skill-union/skillmirror-conformance.sh drives
# `repos.sh digest` over the shared vector table, asserts it equals the raw sha256 of each vector, and
# asserts that at least one vector makes it DIFFER from the library's measured canonical digest — so a
# silent convergence reds exactly as loudly as a silent divergence (the both-directions rule the
# `divergence` blocks in skillmirror.fixtures.json already apply to `verify`).
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
  local cap="" all=0 field="full" reg="$REG_DEFAULT" delivery=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --receives)     need_val list "$@"; cap="$2";      shift 2 ;;
      --all)          all=1;                             shift 1 ;;
      --field)        need_val list "$@"; field="$2";    shift 2 ;;
      --kit-delivery) need_val list "$@"; delivery="$2"; shift 2 ;;
      --registry)     need_val list "$@"; reg="$2";      shift 2 ;;
      *)              die "list: unknown arg '$1'." ;;
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
  # --kit-delivery filters coordination-kit receivers by HOW they take the kit (ADR-0062, #1287):
  # 'byte-copy' (the default when the row omits it) or 'package'. coordination-propagate asks for the
  # byte-copy set so it SKIPS a receiver that has switched to the FS.GG.Kit package; coordination-
  # coherence asks for none and checks them all. Absent field == 'byte-copy', so the filter is a pure
  # narrowing that adding the field to no row changes nothing.
  case "$delivery" in ""|byte-copy|package) ;; *) die "list: --kit-delivery must be byte-copy or package." ;; esac
  if [ -n "$delivery" ] && [ "$all" = 1 ]; then
    die "list: --kit-delivery filters coordination-kit receivers; use it with --receives, not --all."
  fi
  [ -f "$reg" ] || die "registry not found: $reg"
  if [ "$all" = 1 ]; then
    yaml2json "$reg" | jq -r --arg f "$field" '.repos[] | .[$f]'
  else
    yaml2json "$reg" | jq -r --arg cap "$cap" --arg f "$field" --arg d "$delivery" \
      '.repos[]
       | select((.receives // []) | index($cap))
       | select($d == "" or ((."kit-delivery" // "byte-copy") == $d))
       | .[$f]'
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
#   materializer: <id>  PULL, by a package materializer. The receiver explicitly opts into the
#                       materialized capability in its FS.GG.Kit receiver project AND its CI reruns
#                       materialization and fails on drift. `build-config` is the currently supported
#                       materializer detector.
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
# With no --field this emits a TSV row per capability — the audit needs every column at once, and seven
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
  case "$field" in ""|id|workflow|script|materializer|caller|push|receivers|reason) ;;
    *) die "caps: --field must be id, workflow, script, materializer, caller, push, receivers or reason." ;; esac
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
           (.materializer // ""), (.caller // ""),
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

# ---- the scoped branch-protection writer (.github#1613) -----------------------------------------
#
# WHY IT EXISTS. `.github#1587` needs the kit-bump diff-shape guard (`scripts/check-kit-bump-shape.py`,
# #1693) to be a REQUIRED status context in each kit receiver, because Renovate's `automergeType: pr`
# merges through GitHub's merge API, which consults branch protection and nothing else — a check that
# is merely PRESENT is one automerge walks straight past. Before this, the only branch-protection
# access anywhere in scripts/ was a single GET (`check-required-contexts.py`), so arming seven repos
# meant seven hand-edits with no dry run, no read-back, and no record of what changed.
#
# WHAT IT MUST NEVER BECOME, and this is the ITEM, not decoration. This tool can weaken protection
# across the whole roster. **Adding a required context and REMOVING one are different operations with
# different authority.** So they are different SUBCOMMANDS, and the removal one additionally demands
# `--confirm-remove <the exact context name>`. There is deliberately no `--remove` flag, no `--op`, no
# `--mode`: nothing a caller types on `require-context` can make it delete anything, because the verb
# is what selects the operation and the verb is not an argument.
#
# THE ENDPOINTS ARE THE REAL GUARANTEE, not the flag parsing. This script names exactly two API paths
# (`rsc_read_path`, `rsc_write_path`) and both live under `…/protection/required_status_checks`:
#
#   GET    …/protection/required_status_checks            read the current contexts
#   POST   …/protection/required_status_checks/contexts   APPEND contexts — cannot delete
#   DELETE …/protection/required_status_checks/contexts   delete named contexts
#
# The `contexts` FIELD is deprecated in favour of `checks` (a `{context, app_id}` array), and the
# collection sub-resource above is chosen anyway, deliberately. `checks` is only reachable through a
# PATCH of the whole `required_status_checks` object, which means read-modify-write on a structure
# that also carries `strict` — one more field a bug can clear. The collection endpoint takes a delta,
# so there is nothing to accidentally omit. It also registers the context with `app_id: null`, which
# is "any app may report this" — the correct and non-narrowing choice for a context produced by
# whichever workflow run happens to report it. The GET below reads BOTH shapes, so this tool is
# correct against a branch that has already migrated.
#
# It NEVER names `…/branches/<b>/protection` itself. That matters more than any check we could write:
# the bare protection endpoint is a whole-object PUT, so a payload that merely OMITS `enforce_admins`
# or `required_pull_request_reviews` DISABLES them. A tool that used it would be one forgotten field
# away from unprotecting seven repositories. The narrow resource this uses does not contain those
# fields at all, so no bug in this file can reach them. `enforce_admins: true` is live on several of
# these repos; nothing here can see it, let alone change it.
#
# IT NEVER CREATES PROTECTION. A 404 from the read means the branch has no CLASSIC required-status-
# checks block, and the only way to create one is the whole-object PUT above. So a 404 is reported as
# a per-repo FAILURE and the sweep exits non-zero — arming that repo is a human decision about how a
# branch is governed, not a side effect of adding one context.
#
# CLASSIC STORE ONLY, SAID OUT LOUD. A branch may be governed by classic protection, by a REPOSITORY
# RULESET, or by both, and the two stores do not report each other (`check-required-contexts.py`'s
# header, #574). The two stack, so making a context required in the classic store is SUFFICIENT for it
# to be required — which is all the add path claims. The remove path claims less, and says so: it
# removes from the classic store, and a ruleset may still require the context afterwards. Under-
# promising is the safe direction for a removal.
#
# CREDENTIAL, MEASURED 2026-07-28, AND THIS IS NOT AUTOMATION YET. Reading protection needs
# `administration: read`; writing needs `administration: write`. NEITHER is a valid `permissions:`
# scope for a workflow's GITHUB_TOKEN — declaring one is a startup validation error that produces no
# check run at all (#478's blind spot, hit for real by #1575). The org dispatch App holds
# `administration: READ` (the #574 org grant, 2026-07-17) and that is what
# required-context-coherence.yml mints. **No credential in this org can run the APPLY path today**:
# raising the App's grant to `administration: write` is an org-owner action, not a change in this
# repo. So this ships as an OPERATOR tool driven by a `GH_TOKEN` that carries the scope (an App token
# minted from a raised grant, or a human PAT with admin on the target repos), and it says so when the
# token cannot: a 403 on the write is reported as "this token lacks administration: write on <repo>",
# never as a generic outage. The DRY RUN needs only `administration: read`, so it is runnable from the
# existing App grant today and is the half that can be automated now.
#
# DRY RUN BY DEFAULT, READ BACK ALWAYS, FAIL CLOSED ON PARTIAL APPLICATION. Nothing is written without
# `--apply`. After every write the contexts are RE-READ and compared against the exact expected set —
# an HTTP 200 is not evidence, and a write that returns 200 while the read-back disagrees is a
# failure, not a success. If it arms four repos and the fifth fails, it reports every repo's outcome,
# exits non-zero, and does not retry: a half-armed roster is worse than none because it looks done.
# `--only` exists for exactly that recovery — it NARROWS the roster-derived set and can never extend
# it, so finishing a partial application is still a roster-derived operation, not a hand list.
#
# THE TARGET SET IS THE ROSTER. `--receives <cap>` resolves through the same query every other fabric
# uses. There is no `--repo`, no repo list, and no file of targets: a list here would be the fifth
# copy of a fact that already exists, which is the defect class #1507/#1510/#1515/#1528/#1538 closed.

# THE ONLY TWO API PATHS THIS SCRIPT EVER NAMES. Functions, so there is exactly one literal of each
# to audit — tests/repos-registry/run.sh greps this file for the forbidden ones.
rsc_read_path()  { printf 'repos/%s/branches/%s/protection/required_status_checks' "$1" "$2"; }
rsc_write_path() { printf 'repos/%s/branches/%s/protection/required_status_checks/contexts' "$1" "$2"; }

GH_OUT=""; GH_ERR=""
# gh_api_capture <gh api args…> — stdout into GH_OUT, stderr into GH_ERR, gh's rc returned. stderr is
# captured rather than passed through because the HTTP STATUS is a verdict here: 404 means "no classic
# block" (a finding about the repo) and 403 means "this token cannot" (a finding about the credential),
# and collapsing them into "it failed" is the #320 conflation this repo has paid for twice.
gh_api_capture() {
  local errf rc=0
  errf="$(mktemp "${TMPDIR:-/tmp}/repos-protect.XXXXXX")"
  GH_OUT="$(gh api "$@" 2>"$errf")" || rc=$?
  GH_ERR="$(cat "$errf")"; rm -f "$errf"
  return "$rc"
}
# The HTTP status gh named on the last call, or "" if it named none.
gh_http_status() { printf '%s' "$GH_ERR" | sed -n 's/.*(HTTP \([0-9][0-9]*\)).*/\1/p' | tail -n1; }

# read_contexts <repo> <branch> — the CURRENT required contexts, one per line, sorted.
# rc 0 = read it; 4 = no classic required-status-checks block (404); 5 = could not read.
# The two stores GitHub keeps are unioned at READ time by check-required-contexts.py; here only the
# classic one is read, because it is the only one this tool can write and reporting a ruleset context
# in `before=` would imply this tool could remove it.
read_contexts() {
  local rc=0
  gh_api_capture "$(rsc_read_path "$1" "$2")" || rc=$?
  if [ "$rc" -ne 0 ]; then
    case "$(gh_http_status)" in 404) return 4 ;; *) return 5 ;; esac
  fi
  # BOTH shapes: `contexts` is the deprecated string array and `checks` the current
  # {context, app_id} array. A repo migrated to `checks` reports an EMPTY `contexts`, and reading only
  # that would say "nothing is required here" about a fully protected branch — then the add path would
  # report every context as newly added and the read-back comparison would be against a fiction.
  printf '%s' "$GH_OUT" | jq -r '[ (.contexts // [])[], ((.checks // [])[] | .context) ] | unique | .[]'
}

fmt_set() { paste -sd',' - 2>/dev/null | sed 's/,/, /g'; }

# scope_hint <read|write> — the extra sentence a 403 earns, and only a 403. A missing SCOPE and a
# transient outage look identical in a diagnostic that just prints the error, and they are repaired
# completely differently: one is an org-owner grant, the other is waiting. Neither read nor write
# scope is grantable to a workflow GITHUB_TOKEN (#478/#1575), so the hint has to name what WOULD work.
scope_hint() {
  [ "$(gh_http_status)" = 403 ] || return 0
  if [ "$1" = write ]; then
    printf ' — this token lacks administration: write on it. No GITHUB_TOKEN can hold that scope; use an App token minted from an administration:write grant, or a PAT with admin on this repo.'
  else
    printf ' — this token lacks administration: read on it. The org dispatch App holds that grant (#574); a GITHUB_TOKEN cannot.'
  fi
}

# protect_sweep <op> <ctx> <cap> <branch> <apply> <only> <registry>
#
# `<op>` is a LITERAL written by the subcommand — `cmd_require_context` passes `add`, and
# `cmd_unrequire_context` is the only thing in this file that passes `remove`. It is never derived
# from argv, so there is no command line that makes the add verb delete. That is the invariant
# tests/repos-registry/run.sh mutates and asserts, at the source AND on the recorded API traffic.
protect_sweep() {
  local op="$1" ctx="$2" cap="$3" branch="$4" apply="$5" only="$6" reg="$7"
  # A context name is compared as a LINE — `grep -Fx`, and set equality over newline-separated lists.
  # A multi-line value would silently mean something else to every one of those comparisons, and the
  # place that would first show up is the read-back saying "match" about a set it never checked. No
  # real GitHub context contains a newline, so this is a refusal, not a limitation.
  case "$ctx" in
    *$'\n'*) die "$op-context: --context must be a SINGLE LINE — every comparison in this tool is line-oriented, and a multi-line context would make the read-back verify something other than what was written." ;;
  esac
  command -v gh >/dev/null 2>&1 || die "gh not found (required by $op-context)."
  [ -f "$reg" ] || die "registry not found: $reg"

  local targets; targets="$(cmd_list --receives "$cap" --field full --registry "$reg")"
  [ -n "$targets" ] \
    || die "no repo in the roster receives '$cap' — refusing to sweep an EMPTY target set, which would report success having armed nothing (#503's vacuous green)."
  if [ -n "$only" ]; then
    # NARROWS, never extends. `--only` naming a repo outside the capability's roster set would be a
    # hand-picked target, which is the whole thing this tool refuses to have.
    printf '%s\n' "$targets" | grep -Fxq -- "$only" \
      || die "--only '$only' is not among the repos that receive '$cap' — it narrows the roster-derived set and can never extend it. Roster set: $(printf '%s\n' "$targets" | fmt_set)"
    targets="$only"
  fi

  echo "repos-protect: op=$op context='$ctx' capability=$cap branch=$branch apply=$apply store=classic"
  [ "$apply" = yes ] || echo "repos-protect: DRY RUN — nothing will be written. Re-run with --apply to write."

  local nchange=0 nsame=0 nfail=0 repo before after want rc
  while IFS= read -r repo; do
    [ -n "$repo" ] || continue
    rc=0; before="$(read_contexts "$repo" "$branch")" || rc=$?
    if [ "$rc" -eq 4 ]; then
      echo "::error::repos-protect: $repo: branch '$branch' has NO classic required-status-checks block. This tool will not CREATE one — that is a whole-object PUT of the protection resource, the one call that can disable enforce_admins or review requirements by omission. Arm the branch by hand, then re-run." >&2
      nfail=$((nfail + 1)); continue
    elif [ "$rc" -ne 0 ]; then
      echo "::error::repos-protect: $repo: could not READ $branch's required status checks$(scope_hint read) — $GH_ERR" >&2
      nfail=$((nfail + 1)); continue
    fi

    # The expected post-state, computed BEFORE any write, so the read-back has something independent
    # to be compared against. `add` is a union and `remove` a difference; both are exact.
    #
    # `|| true` on both, and it is not defensive noise: with `pipefail`, a `grep` that matches nothing
    # returns 1, and the two cases where it matches nothing are REAL — an empty `before`, and removing
    # the last remaining required context. Without it the sweep would `set -e` out mid-roster, having
    # written to some repos and reported on none of them, which is the fail-open this whole tool is
    # built to refuse.
    if [ "$op" = add ]; then
      want="$(printf '%s\n%s\n' "$before" "$ctx" | grep -v '^$' | LC_ALL=C sort -u || true)"
    else
      want="$(printf '%s\n' "$before" | grep -v '^$' | grep -Fxv -- "$ctx" | LC_ALL=C sort -u || true)"
    fi

    if [ "$want" = "$before" ]; then
      if [ "$op" = remove ]; then
        # Naming a context that is not there is not a no-op worth being quiet about: the operator
        # believes they are removing something, and the tool disagreeing silently is how a typo
        # becomes "done". A removal must name an EXISTING context, exactly.
        echo "::error::repos-protect: $repo: '$ctx' is NOT among $branch's classic required contexts, so there is nothing to remove. before=[$(printf '%s\n' "$before" | fmt_set)]" >&2
        nfail=$((nfail + 1)); continue
      fi
      echo "repos-protect: $repo  before=[$(printf '%s\n' "$before" | fmt_set)]  after=[$(printf '%s\n' "$before" | fmt_set)]  UNCHANGED (already required)"
      nsame=$((nsame + 1)); continue
    fi

    if [ "$apply" != yes ]; then
      echo "repos-protect: $repo  before=[$(printf '%s\n' "$before" | fmt_set)]  after=[$(printf '%s\n' "$want" | fmt_set)]  WOULD-$(echo "$op" | tr '[:lower:]' '[:upper:]')"
      nchange=$((nchange + 1)); continue
    fi

    rc=0
    case "$op" in
      # APPEND-ONLY by construction: POST to the contexts collection cannot delete a member. This is
      # the strongest statement available that the add path cannot reduce protection — it is not a
      # guard that could be bugged around, it is an endpoint that has no delete semantics.
      add)    gh_api_capture --method POST   "$(rsc_write_path "$repo" "$branch")" -f "contexts[]=$ctx" >/dev/null || rc=$? ;;
      remove) gh_api_capture --method DELETE "$(rsc_write_path "$repo" "$branch")" -f "contexts[]=$ctx" >/dev/null || rc=$? ;;
      *)      die "protect_sweep: unknown op '$op' — this is a bug in repos.sh, not a call error." ;;
    esac
    if [ "$rc" -ne 0 ]; then
      echo "::error::repos-protect: $repo: the $op WRITE failed$(scope_hint write) — $GH_ERR" >&2
      # NO BLIND RETRY. A write that failed for an unknown reason may have partially applied, and a
      # second attempt cannot tell "it did not happen" from "it happened and the response was lost".
      nfail=$((nfail + 1)); continue
    fi

    # READ BACK. Never infer success from the write's own response — that response is the same server
    # telling us what it thinks it did, and the whole point of the read is a second, independent
    # observation. It is compared for EQUALITY with `want`, not merely for `$ctx` being present:
    # equality is what catches a context that vanished while we were adding a different one.
    rc=0; after="$(read_contexts "$repo" "$branch")" || rc=$?
    if [ "$rc" -ne 0 ]; then
      echo "::error::repos-protect: $repo: the $op write reported success and the READ-BACK failed, so the applied state is UNKNOWN — re-read it by hand before assuming anything: $GH_ERR" >&2
      nfail=$((nfail + 1)); continue
    fi
    if [ "$after" != "$want" ]; then
      echo "::error::repos-protect: $repo: READ-BACK MISMATCH — expected=[$(printf '%s\n' "$want" | fmt_set)] actual=[$(printf '%s\n' "$after" | fmt_set)]. The write returned success and the branch does not hold the expected set; an HTTP 200 is not evidence. NOT retried." >&2
      nfail=$((nfail + 1)); continue
    fi
    echo "repos-protect: $repo  before=[$(printf '%s\n' "$before" | fmt_set)]  after=[$(printf '%s\n' "$after" | fmt_set)]  $(echo "${op}ED" | tr '[:lower:]' '[:upper:]')"
    nchange=$((nchange + 1))
  done < <(printf '%s\n' "$targets")

  local verb="would-$op"
  if [ "$apply" = yes ]; then verb="${op}ed"; fi
  echo "repos-protect: $((nchange + nsame + nfail)) repo(s): $nchange $verb, $nsame unchanged, $nfail failed"
  if [ "$nfail" -gt 0 ]; then
    echo "::error::repos-protect: FAILED on $nfail repo(s) — see the per-repo lines above. A partially armed roster is worse than an unarmed one because it looks done, so this exits non-zero and nothing was retried." >&2
    exit 1
  fi
}

# ADD ONLY. There is no flag here that removes anything, and `--confirm-remove` is deliberately an
# UNKNOWN ARG rather than an accepted-and-ignored one: a flag that is tolerated on the wrong verb is a
# flag someone will believe did something.
cmd_require_context() {
  local ctx="" cap="" branch="main" apply=no only="" reg="$REG_DEFAULT"
  while [ $# -gt 0 ]; do
    case "$1" in
      --context)   need_val require-context "$@"; ctx="$2";    shift 2 ;;
      --receives)  need_val require-context "$@"; cap="$2";    shift 2 ;;
      --branch)    need_val require-context "$@"; branch="$2"; shift 2 ;;
      --only)      need_val require-context "$@"; only="$2";   shift 2 ;;
      --registry)  need_val require-context "$@"; reg="$2";    shift 2 ;;
      --apply)     apply=yes; shift 1 ;;
      *)           die "require-context: unknown arg '$1'. This verb ADDS a required context and does nothing else — removal is 'repos.sh unrequire-context', a separate verb with a separate confirmation (#1613)." ;;
    esac
  done
  [ -n "$ctx" ] || die "require-context: --context <name> is required."
  [ -n "$cap" ] || die "require-context: --receives <capability> is required — the target set is derived from the roster, never hand-listed."
  protect_sweep add "$ctx" "$cap" "$branch" "$apply" "$only" "$reg"
}

# REMOVE ONLY, and the confirmation is the point. `--confirm-remove` must REPEAT the exact context
# name: a bare boolean flag can be pasted from a previous command and carry over to a context it was
# never meant for, whereas a value that must equal `--context` cannot be reused without being reread.
# Weakening protection should cost a deliberate act of typing.
cmd_unrequire_context() {
  local ctx="" cap="" branch="main" apply=no only="" reg="$REG_DEFAULT" confirm="" sawconfirm=no
  while [ $# -gt 0 ]; do
    case "$1" in
      --context)        need_val unrequire-context "$@"; ctx="$2";    shift 2 ;;
      --receives)       need_val unrequire-context "$@"; cap="$2";    shift 2 ;;
      --branch)         need_val unrequire-context "$@"; branch="$2"; shift 2 ;;
      --only)           need_val unrequire-context "$@"; only="$2";   shift 2 ;;
      --registry)       need_val unrequire-context "$@"; reg="$2";    shift 2 ;;
      --confirm-remove) need_val unrequire-context "$@"; confirm="$2"; sawconfirm=yes; shift 2 ;;
      --apply)          apply=yes; shift 1 ;;
      *)                die "unrequire-context: unknown arg '$1'." ;;
    esac
  done
  [ -n "$ctx" ] || die "unrequire-context: --context <name> is required."
  [ -n "$cap" ] || die "unrequire-context: --receives <capability> is required — the target set is derived from the roster, never hand-listed."
  [ "$sawconfirm" = yes ] \
    || die "unrequire-context: REFUSING to remove a required context without --confirm-remove '$ctx'. Removing a required status check lowers a repository's merge bar, so it is a deliberate act with its own confirmation and never a side effect of another operation (#1613)."
  [ "$confirm" = "$ctx" ] \
    || die "unrequire-context: --confirm-remove '$confirm' does not match --context '$ctx'. Name the exact context being removed — a confirmation that does not have to be reread is not a confirmation."
  protect_sweep remove "$ctx" "$cap" "$branch" "$apply" "$only" "$reg"
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
  local id full role recv delivery cap
  while IFS=$'\t' read -r id full role recv delivery; do
    [ -n "$id" ] || continue
    [[ "$id"   =~ ^[a-z0-9.][a-z0-9._-]*$ ]] || err "repo id '$id' must be lowercase kebab/dotted."
    [[ "$full" =~ ^FS-GG/.+ ]]               || err "repo '$id' full '$full' must be 'FS-GG/<repo>'."
    case "$role" in authority|framework) ;; *) err "repo '$id' role '$role' invalid (authority|framework)." ;; esac
    for cap in $recv; do
      echo "$KNOWN_CAPS" | jq -e --arg c "$cap" 'index($c)' >/dev/null \
        || err "repo '$id' receives unknown capability '$cap' (known: $(echo "$KNOWN_CAPS" | jq -r 'join(", ")'))."
    done
    # kit-delivery (ADR-0062, #1287): how this repo takes the coordination kit. Optional; the ABSENCE
    # means 'byte-copy', so it is not written on a row until that row switches to 'package'. It is
    # meaningful ONLY on a coordination-kit receiver — set on anything else it is a silent no-op that
    # reads as intent, so reject it. '@tsv' emits an empty field when the key is absent.
    case "$delivery" in
      "") ;;
      byte-copy|package)
        echo "$recv" | grep -qw coordination-kit \
          || err "repo '$id' sets kit-delivery '$delivery' but does not receive coordination-kit — the field only governs how a coordination-kit receiver takes the kit." ;;
      *) err "repo '$id' kit-delivery '$delivery' invalid (byte-copy|package)." ;;
    esac
  done < <(echo "$json" | jq -r '.repos[] | [.id, .full, .role, ((.receives // []) | join(" ")), (."kit-delivery" // "")] | @tsv')

  # --- exactly one authority, matching the top-level, and not a kit receiver ---
  local authct; authct="$(echo "$json" | jq '[.repos[] | select(.role=="authority")] | length')"
  if [ "$authct" != "1" ]; then
    err "exactly one repo must have role 'authority' (found $authct)."
  else
    local authfull; authfull="$(echo "$json" | jq -r '.repos[] | select(.role=="authority") | .full')"
    [ "$authfull" = "$authority" ] || err "authority repo '$authfull' != top-level authority '$authority'."
  fi
  # THE AUTHORITY MAY NOT RECEIVE A CAPABILITY IT IS THE SOURCE OF, and this is a LIST because the
  # failure it prevents is not cosmetic. `repos-audit` detects a receiver by looking for a `uses:` of the
  # AUTHORITY's copy, and deliberately does not match a repo running its own — so for these capabilities
  # `.github` can never satisfy the detector by any edit to `.github`. Rostering it therefore produces a
  # gap that is permanent, unfixable, and describes the repo falsely ("nothing calls it" when the repo IS
  # it). `coordination-kit` has been guarded since the roster existed; `skill-union` needs it for exactly
  # the same reason — `.github` asserts its own roots with skill-roots-selfcheck.yml, its own workflow.
  # A string-equality fixture over today's roster is not this guard: it pins the DATA, and this pins the
  # INVARIANT (#1504 review).
  local srccap
  for srccap in coordination-kit skill-union; do
    if echo "$json" | jq -e --arg a "$authority" --arg c "$srccap" \
        '.repos[] | select(.full==$a and ((.receives // []) | index($c)))' >/dev/null; then
      err "authority repo '$authority' must not RECEIVE $srccap — it is the SOURCE. repos-audit detects a receiver by a 'uses:' of the authority's own workflow and never matches a repo running its own, so this row can only ever be an unfixable gap."
    fi
  done

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
  # form never had an empty MIDDLE field, so it worked by luck; a 7-column row with four optional
  # detectors has one on almost every line. Re-delimit with a unit separator, which is NOT IFS
  # whitespace and therefore preserves empties. (jq's @tsv escapes any literal tab in a value, so the
  # substitution cannot corrupt a `reason:`.)
  local cid cwf cscript cmaterializer ccaller cpush crecv creason line
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    IFS=$'\x1f' read -r cid cwf cscript cmaterializer ccaller cpush crecv creason <<< "${line//$'\t'/$'\x1f'}"
    [ -n "$cid" ] || continue
    echo "$KNOWN_CAPS" | jq -e --arg c "$cid" 'index($c)' >/dev/null \
      || err "capability '$cid' is not in the receives vocabulary (known: $(echo "$KNOWN_CAPS" | jq -r 'join(", ")'))."

    # EXACTLY ONE DETECTOR (#628). Zero is the defect this rule exists to kill: a capability with no
    # detector is swept in NEITHER direction, so it cannot be found unwired and cannot be found
    # adopted-but-unrostered — it is simply outside the audit, silently, while remaining a legal
    # `receives:` word. Two is ambiguous: repos-audit would have to pick one, and a receiver that
    # satisfies the loose one would mask a gap in the strict one.
    local ndet=0
    [ -n "$cwf" ]          && ndet=$((ndet + 1))
    [ -n "$cscript" ]      && ndet=$((ndet + 1))
    [ -n "$cmaterializer" ] && ndet=$((ndet + 1))
    [ -n "$ccaller" ]      && ndet=$((ndet + 1))
    [ "$cpush" = true ]    && ndet=$((ndet + 1))
    if [ "$ndet" -eq 0 ]; then
      err "capability '$cid' declares no detector — set 'workflow:' (a reusable workflow the receiver calls), 'script:' (an authority script the receiver runs from an inlined job), 'materializer:' (an explicit package-materializer opt-in plus CI drift enforcement), 'caller:' (a compound receiver-caller contract — the receiver calls a reusable workflow AGAINST A PARTICULAR SUBJECT, and a bare 'uses:' cannot tell that subject from another), or 'push: true' with a reason (the authority writes it INTO the receiver; nothing is detectable receiver-side). A capability with no detector is audited in NEITHER direction while still being a legal 'receives' word, which is exactly how build-config came to be received by four repos and checked by nothing (#628)."
    elif [ "$ndet" -gt 1 ]; then
      err "capability '$cid' declares more than one detector (workflow/script/materializer/caller/push) — a capability is verified ONE way; two lets a receiver satisfy the loose one and mask a gap in the strict one."
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

    # Package materializers are compound receiver-side contracts. A receiver counts only when it
    # explicitly opts into the materialized surface AND CI proves the committed result is current.
    # Keep the detector vocabulary closed: accepting an unknown id here would make repos-audit sweep
    # it with no implementation and turn a schema typo into a confident gap for every receiver.
    case "$cmaterializer" in
      "") ;;
      build-config) ;;
      *) err "capability '$cid' names unsupported materializer detector '$cmaterializer' (supported: build-config)." ;;
    esac

    # The CALLER detector (#1504). Same closed-vocabulary rule as `materializer:` and, on top of it,
    # the same SUBJECT-EXISTS rule as `workflow:`/`script:` — because a caller detector greps receivers
    # for a `uses:` of a named reusable workflow, so a subject that is missing or not `workflow_call`-able
    # reports every receiver unwired forever, which is the fail-open these checks exist to close.
    #
    # WHY `caller:` IS NOT JUST `workflow: skill-union-assert.yml`. The reusable assertion is
    # subject-parameterised: `skill-union-assert.yml` audits whatever `product-path:` names, and
    # FS.GG.Templates legitimately calls it against GENERATED composition products. A bare `uses:`
    # detector cannot tell "this repo gates its OWN committed skill roots" from "this repo gates a
    # scaffolded artifact it produced" — so it would certify the full-union capability off a call that
    # never looks at the receiver's committed roots. That is a green nobody earned (#628), one layer in:
    # the detector's subject is not the workflow, it is what the workflow was pointed at.
    case "$ccaller" in
      "") ;;
      skill-union)
        # `.yml` OR `.yaml`, because the detector matches `skill-union-assert.ya?ml` — a validator that
        # accepted only one spelling would call the roster invalid over a rename the audit handles fine.
        local csubj=""
        [ -f "$root/.github/workflows/skill-union-assert.yml" ]  && csubj="skill-union-assert.yml"
        [ -n "$csubj" ] || { [ -f "$root/.github/workflows/skill-union-assert.yaml" ] && csubj="skill-union-assert.yaml"; }
        if [ -z "$csubj" ]; then
          err "capability '$cid' names caller detector 'skill-union', whose subject .github/workflows/skill-union-assert.yml is not in .github/workflows/ — the audit greps receivers for a 'uses:' of it, so a subject that does not exist reports every receiver unwired."
        elif ! grep -qE '^[[:space:]]*workflow_call:' "$root/.github/workflows/$csubj"; then
          err "capability '$cid' names caller detector 'skill-union', but .github/workflows/$csubj has no 'workflow_call:' trigger — it is not reusable, so no repo can wire it."
        fi ;;
      *) err "capability '$cid' names unsupported caller detector '$ccaller' (supported: skill-union)." ;;
    esac

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
  done < <(echo "$json" | jq -r '(.capabilities // [])[] | [.id, (.workflow // ""), (.script // ""), (.materializer // ""), (.caller // ""), (if .push == true then "true" else "" end), (.receivers // ""), (.reason // "")] | @tsv')

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
    || err "capability/ies received but NOT detectable — no 'capabilities:' row, so repos-audit sweeps them in NEITHER direction and the roster's claim about them can never go red: $undetectable. Give each a detector row (workflow:/script:/materializer:/push:)."

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

  # --- THE #1696 INVARIANT: skill-view and the two libraries it SOURCES ride the kit TOGETHER ---
  # Exactly #1077's shape, one tool over, and it is not hypothetical: `scripts/skill-view` does
  # `. lib/args.sh` and `. lib/roots.sh` at startup (lines 87-89), before parsing a single argument,
  # so a receiver holding the tool without either library has a replacement that exits non-zero on
  # every invocation — and ADR-0067 §9's precondition 1 ("the replacement is present and executable
  # in R") would read as SATISFIED, because the file is right there. That is the coherent-but-
  # unusable class (#1504), and the retirement order's whole per-repo gate rests on that precondition
  # meaning what it says. Keyed on delivered PATHS, not row ids, so a rename cannot slip past.
  local viewlibs; viewlibs="$(echo "$json" | jq -r '
    ( [ (.kit // [])[] | if .kind=="client" then .source elif .kind=="config" then .dest else empty end ] ) as $d
    | if ($d | index("scripts/skill-view")) | not then ""
      else [ "scripts/lib/args.sh", "scripts/lib/roots.sh" ] | map(select(. as $l | ($d | index($l)) | not)) | join(" ")
      end')"
  [ -z "$viewlibs" ] \
    || err "the coordination kit delivers scripts/skill-view but NOT the librar(y|ies) it sources at startup: $viewlibs — a receiver would get ADR-0067 §9's replacement in a form that cannot run, while precondition 1 read as satisfied (#1696, and #1077's shape one tool over). Add a 'kind: config' kit row for each missing path, or remove the skill-view row."

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
  # TWO VERBS, TWO AUTHORITIES (#1613). The operation is selected HERE, by the word the caller typed,
  # and is never an argument to either — so no flag on `require-context` can make it remove, and the
  # removal verb cannot be reached by accident from the routine one.
  require-context)   shift; cmd_require_context "$@" ;;
  unrequire-context) shift; cmd_unrequire_context "$@" ;;
  digest)   shift; [ $# -ge 1 ] || die "digest: <path> required."; digest "$1" ;;
  -h|--help|help|"") usage ;;
  *)        die "unknown command '${1:-}' (try: repos.sh --help)." ;;
esac
