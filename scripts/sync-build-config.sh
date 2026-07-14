#!/usr/bin/env bash
# Distribute the FS-GG org-shared .NET build configuration into a consumer repo.
#
# Source of truth: this repo's dist/dotnet/ (Directory.Build.props, Directory.Packages.props,
# .config/dotnet-tools.json). See docs/build/README.md for the adoption model and the
# unified lockfile-restore-enforcement gate (ADR-0006).
#
# Usage:
#   scripts/sync-build-config.sh <path-to-consumer-repo>          # write/update the managed files
#   scripts/sync-build-config.sh --check <path-to-consumer-repo>  # drift check only; exit 1 on drift
#   scripts/sync-build-config.sh --adopt <path-to-consumer-repo>  # first-time: move an existing
#                                                                  # non-canonical *.props to *.local.props
#                                                                  # (imported) and a hand-authored
#                                                                  # dotnet-tools.json to *.local.json
#                                                                  # (backup only), then write the
#                                                                  # canonical files
#
# Hand-authored-file safety (.github#387): a *.props carries a marker, so apply REFUSES to overwrite
# an unmarked one (run --adopt to move it to an imported *.local.props). The tool manifest is JSON
# with no marker and no *.local override, and re-sync overwrites it by design — so apply only WARNS
# before a content-changing overwrite, while --adopt keeps a *.local.json backup (not re-imported).
#
# --check is the hook the reusable coherence workflow (.github#18) calls in every repo's CI:
# a repo that has hand-edited a managed file fails the gate.
#
# --check compares against THE COMMIT THE RECEIVER IS PINNED TO, not against main (.github#592). A
# receiver that is merely BEHIND is green; only a hand-edited managed file is red. See the PIN_REL
# block below for why — it is the whole point of that change, and reading it before touching --check
# will save you from re-introducing a race that merge-froze this org twice.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/../dist/dotnet" && pwd)"

# Absolute, resolved path to this script. The --check failure message must name a command the reader
# can actually RUN, and the reader is almost never standing where this script lives (.github#633):
# every receiver's CI checks this repo out into a scratch dir and invokes it against a separate
# checkout of the repo under test, so "scripts/sync-build-config.sh" is No such file or directory in
# the repo whose gate just went red. $0 alone is not enough either — it is relative to the invoking
# cwd, so it stops resolving the moment the reader cd's anywhere.
SELF="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"

# Managed files, relative to the repo root.
#
# ADDING A FILE HERE MERGE-FREEZES EVERY RECEIVER THAT DOES NOT ALREADY HAVE IT. `--check` treats a
# missing managed file as DRIFT (see the `check` arm below), and the drift check is a REQUIRED check
# in adopting repos. So the moment a name lands in this list, every repo without that file goes red
# on a check it cannot make green from its own PR — which is a merge freeze, not a nudge.
#
# This is not hypothetical. #499 moved the source of truth in Directory.Build.props and FS.GG.SDD has
# been unable to merge ANYTHING since (.github#379) — a finished, green PR sat blocked for hours. The
# rule that avoids it is the same one ADR-0032 §3 states for lock files: THE RECEIVERS ADOPT FIRST,
# and the shared config starts enforcing it LAST.
#
# So the order for any new managed file is:
#   1. add it under dist/dotnet/ (harmless: not in this list, so nothing checks it yet)
#   2. one PR per receiver, adopting it
#   3. ONLY THEN add its name here — and now the gate is asserting something already true
#
# `global.json` is at step 1 right now (.github#536). It is in dist/dotnet/ and DELIBERATELY not in
# this list: four of the five consumers (Game, Rendering, SDD, Governance) have no global.json at all,
# so adding it today would freeze all four. tests/sync-build-config asserts that it stays out until
# the adoption items land — a comment is not a gate (#266), so the ordering rule is a test.
FILES=(
  "Directory.Build.props"
  "Directory.Packages.props"
  ".config/dotnet-tools.json"
)

# A canonical synced file carries this marker; a hand-authored repo file does not.
MARKER="Source of truth: FS-GG/.github"

# ------------------------------------------------------------------------------------------------
# THE PROVENANCE PIN (.github#592) — why --check does NOT compare against main.
#
# Every receiver's `gate.yml` checks THIS repo out at `ref: main` and runs `--check`. So while --check
# compared the receiver's files against `dist/dotnet/` AS OF MAIN, its verdict was a function of
# ANOTHER REPO'S MOVING BRANCH, evaluated at whatever moment CI happened to run. Two things followed,
# and both are worse than they sound:
#
#   * A receiver could not make the check green FROM ITS OWN PR. Nothing in the PR's tree determined
#     the answer.
#   * The instant anything landed in dist/dotnet/ here, EVERY open PR in EVERY adopting repo went red
#     on a REQUIRED check, through no fault of its own and with no change to its branch.
#
# That is not a drift check, it is a race — and it fired twice. #499 moved Directory.Build.props and
# FS.GG.SDD could merge NOTHING for hours (FS.GG.SDD#379). #536 then edited the same file's XML
# COMMENT — no MSBuild element changed — and red-lit a green, in-flight PR (FS.GG.SDD#380). A comment
# was enough.
#
# So the receiver now records WHICH .github commit its managed files came from, and --check compares
# against THAT commit's dist/dotnet/. The verdict becomes a pure function of the receiver's own tree —
# its files and its pin, both of which arrive together on the PR's own merge ref. Nothing .github does
# to main can move it. The race is gone BY CONSTRUCTION, not by being faster than it.
#
# What the two outcomes now mean — this is the entire point of the change:
#
#   files == dist/dotnet@pin  -> GREEN. The copy is faithful. It may be BEHIND main, and that is FINE:
#                                being behind is not a defect in the PR, and build-config-propagate.yml
#                                (#626) already has a rolling, auto-merging sync PR open to fix it.
#   files != dist/dotnet@pin  -> RED. A managed file was hand-edited. That IS a defect in the PR, and
#                                the author can fix it from their own branch — which is what a required
#                                check is for.
#
# Staleness is therefore VISIBLE (the pin is a committed file you can read) and remediated by a bot,
# rather than "enforced" by freezing everyone's merges. #592 chose that inversion deliberately: for a
# REQUIRED check, stale-until-someone-merges beats merge-frozen-by-default.
#
# THIS FILE IS NOT IN `FILES`, AND MUST NEVER BE ADDED TO IT. FILES is the byte-identity set, and the
# pin is per-receiver by construction (it holds a SHA), so byte-identity is meaningless for it. Worse,
# `--check` treats a MISSING member of FILES as DRIFT — see the FILES warning above — so listing it
# would red-light every receiver that has not been pinned yet. That is the exact merge-freeze this
# change exists to END.
#
# An ABSENT pin is legal, and means LEGACY MODE: compare against main, exactly as before. That is what
# makes this rollout incapable of freezing anyone. A receiver behaves EXACTLY as it does today until
# the propagate bot's next sync PR writes its pin — at which point it stops racing. No receiver
# gate.yml change, no flag day, no per-repo adoption item, no coordinated rollout.
PIN_REL=".config/fsgg-build-config.sha"

# The git checkout THIS SCRIPT lives in — i.e. FS-GG/.github. That is where a pinned commit's
# dist/dotnet/ has to be read from, and it is NOT $TARGET (the receiver never carries .github history).
GITROOT="$(git -C "$(dirname "$SELF")" rev-parse --show-toplevel 2>/dev/null || true)"

# read_pin <repo> -> prints the pinned SHA, or nothing.
# The file is self-describing for humans (see write_pin), so take the first bare 40-hex line and
# ignore comments/blanks. A malformed pin prints nothing and lands in legacy mode — fail-open, which
# is the whole safety posture here: a pin we cannot read must never be able to FAIL a receiver's PR.
read_pin() {
  local f="$1/$PIN_REL"
  [[ -f "$f" ]] || return 0
  grep -oE '^[0-9a-f]{40}$' "$f" 2>/dev/null | head -n1 || true
}

# write_pin <repo> <sha> — record which .github commit these managed files were taken from.
write_pin() {
  local repo="$1" sha="$2"
  mkdir -p "$(dirname "$repo/$PIN_REL")"
  cat > "$repo/$PIN_REL" <<EOF
# The FS-GG/.github commit whose dist/dotnet/ this repo's managed build files were synced from.
#
# The shared-build-config drift check compares those files against THIS commit — not against
# .github@main — so the check's answer depends only on this repo's own tree, and a change landing in
# .github can never red-light an open PR here (.github#592). Being behind main is not a failure: the
# build-config-propagate bot (.github#626) opens a rolling, auto-merging PR that bumps these files and
# this pin together.
#
# Written by scripts/sync-build-config.sh. Do not hand-edit: editing the pin without re-syncing the
# files is how you would tell the gate to check your work against the wrong baseline.
$sha
EOF
  echo "pinned: $PIN_REL -> $sha"
}

# source_sha -> prints the .github commit to record in a receiver's pin, or nothing.
#
# The propagate workflow passes the exact commit it is distributing; a human running the script from a
# checkout gets that checkout's HEAD. If neither is available (no git, a tarball, a detached source
# tree) we print nothing and simply do not write a pin — apply still syncs the files, and the receiver
# stays in legacy mode. Refusing to sync over a missing pin would be a self-inflicted outage.
#
# A DIRTY dist/dotnet/ YIELDS NO PIN, and this is the subtle one. `apply` copies the WORKING TREE, but
# a pin names a COMMIT — so syncing from a checkout with uncommitted changes under dist/dotnet/ would
# hand the receiver files that match no commit, stamped with a pin claiming they match HEAD. --check
# would then resolve HEAD, diff it against files that were never in it, and report DRIFT — accusing an
# innocent receiver of a hand-edit that the operator made HERE. The pin is a factual claim about
# provenance; when we cannot make it truthfully, we make none.
source_sha() {
  if [[ -n "${FSGG_BUILD_CONFIG_SHA:-}" ]]; then
    printf '%s' "$FSGG_BUILD_CONFIG_SHA"; return 0
  fi
  [[ -n "$GITROOT" ]] || return 0
  git -C "$GITROOT" diff --quiet HEAD -- dist/dotnet 2>/dev/null || return 0
  git -C "$GITROOT" rev-parse HEAD 2>/dev/null || true
}

# have_commit <sha> — is that commit present in this .github checkout?
have_commit() { [[ -n "$GITROOT" ]] && git -C "$GITROOT" cat-file -e "${1}^{commit}" 2>/dev/null; }

# materialize_pin <sha> <destdir> — write dist/dotnet/<FILES> AS OF <sha> into <destdir>.
#
# Every receiver's CI checks .github out at depth 1, so the pinned commit is usually NOT in the local
# object store and must be fetched. GitHub serves a bare SHA on request, and a depth-1 fetch of one
# commit brings its trees and blobs with it — which is all we read.
#
# Returns non-zero if the commit cannot be obtained, or if ANY managed file is unreadable at it (e.g. a
# pin predating a file's addition to dist/dotnet/). A PARTIALLY resolved baseline is not a baseline, so
# this is all-or-nothing: the caller falls back to main and says so, rather than silently checking half
# the files against one commit and half against another.
materialize_pin() {
  local sha="$1" dest="$2" rel attempt

  [[ -n "$GITROOT" ]] || return 1

  if ! have_commit "$sha"; then
    # Only worth a network round-trip if there is somewhere to fetch FROM. In the fixture (a bare
    # `git init` with no remote) there is not, and `git fetch origin` would just fail slowly.
    git -C "$GITROOT" remote get-url origin >/dev/null 2>&1 || return 1

    # RETRY. This fetch is the one part of the check that is not a function of the receiver's tree, and
    # a failure here costs more than a slow run: it drops us into the degraded path below. Three tries
    # with a short backoff turns "GitHub blinked" from an outcome into a non-event. It cannot fix a pin
    # that names a commit which does not exist — nothing can — and that case still exits non-zero after
    # three quick, cheap failures.
    for attempt in 1 2 3; do
      git -C "$GITROOT" fetch --quiet --depth 1 origin "$sha" 2>/dev/null && break
      [[ "$attempt" -eq 3 ]] && return 1
      sleep $(( attempt * 2 ))
    done
    have_commit "$sha" || return 1
  fi

  for rel in "${FILES[@]}"; do
    mkdir -p "$dest/$(dirname "$rel")"
    git -C "$GITROOT" show "$sha:dist/dotnet/$rel" > "$dest/$rel" 2>/dev/null || return 1
  done
}

# XML well-formedness guard (.github#29). The drift check compares files byte-for-byte,
# so a malformed-but-verbatim source (e.g. a `--` inside an XML comment) passes --check
# yet fails every adopter's `dotnet restore`/`pack` with MSB4024. Assert the source .props
# are loadable XML BEFORE we distribute or pass-check them, so the source of truth can't
# ship invalid XML again. Prefer xmllint; fall back to python3; warn (don't block) if neither.
assert_source_xml_wellformed() {
  local validator=""
  if command -v xmllint >/dev/null 2>&1; then
    validator="xmllint"
  elif command -v python3 >/dev/null 2>&1; then
    validator="python3"
  else
    echo "WARN: no xmllint or python3 found; skipping XML well-formedness check of source .props" >&2
    return 0
  fi
  local f bad=0
  for f in "$SRC"/*.props; do
    [[ -f "$f" ]] || continue
    case "$validator" in
      xmllint) xmllint --noout "$f" || bad=1 ;;
      python3) python3 -c 'import sys,xml.dom.minidom; xml.dom.minidom.parse(sys.argv[1])' "$f" || bad=1 ;;
    esac
  done
  if [[ "$bad" -ne 0 ]]; then
    echo "Source .props in $SRC are not well-formed XML; refusing to distribute. Fix the source of truth first." >&2
    exit 1
  fi
}
assert_source_xml_wellformed

# JSON well-formedness guard (companion to the XML guard above): the drift check compares
# .config/dotnet-tools.json byte-for-byte, so a malformed source would pass --check yet break every
# adopter's `dotnet tool restore`. Assert the source tool-manifest is valid JSON BEFORE we
# distribute it. Prefer jq; fall back to python3; warn (don't block) if neither is present.
assert_source_json_wellformed() {
  local f="$SRC/.config/dotnet-tools.json"
  [[ -f "$f" ]] || return 0
  if command -v jq >/dev/null 2>&1; then
    jq -e . "$f" >/dev/null || { echo "Source $f is not valid JSON; refusing to distribute. Fix the source of truth first." >&2; exit 1; }
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys,json; json.load(open(sys.argv[1]))' "$f" \
      || { echo "Source $f is not valid JSON; refusing to distribute. Fix the source of truth first." >&2; exit 1; }
  else
    echo "WARN: no jq or python3 found; skipping JSON well-formedness check of $f" >&2
  fi
}
assert_source_json_wellformed

mode="apply"
case "${1:-}" in
  --check) mode="check"; shift ;;
  --adopt) mode="adopt"; shift ;;
  -*) echo "unknown flag: $1" >&2; exit 2 ;;
esac

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
  echo "usage: $(basename "$0") [--check|--adopt] <path-to-consumer-repo>" >&2
  exit 2
fi
TARGET="$(cd "$TARGET" && pwd)"

# ---- Which baseline does --check measure this receiver against? (.github#592) --------------------
#
# BASE is the directory of canonical files to compare with, and BASE_DESC names the commit it came
# from so a red gate can state its own yardstick. Two modes, and the difference is the whole item:
#
#   PINNED  — BASE is dist/dotnet/ AS OF the receiver's pin. The verdict then depends only on the
#             receiver's own tree, so a change landing in .github@main cannot touch it. No race.
#   LEGACY  — no pin: BASE is dist/dotnet/ as checked out (main). Exactly today's behaviour, race and
#             all. This is the state every receiver is in until the propagate bot pins it, and keeping
#             it working is what makes this rollout unable to freeze anybody.
#
# apply/adopt ALWAYS distribute from $SRC — a sync writes what .github ships NOW. Only --check reads
# the pin, so BASE is deliberately consulted in the check arm alone.
BASE="$SRC"
BASE_DESC="dist/dotnet/ as checked out here"
PIN=""
PINDIR=""
BEHIND=""
PIN_UNRESOLVED=""
cleanup() { [[ -z "$PINDIR" ]] || rm -rf "$PINDIR"; }
trap cleanup EXIT

if [[ "$mode" == "check" ]]; then
  PIN="$(read_pin "$TARGET")"
  if [[ -z "$PIN" ]]; then
    echo "NOTE: no $PIN_REL here, so this repo is checked against .github as currently checked out."
    echo "      That answer depends on .github's main AT CI TIME, so an unrelated change there can red-"
    echo "      light this repo's open PRs (.github#592). It is not a fault in this repo and there is"
    echo "      nothing to do: build-config-propagate (#626) writes the pin on its next sync PR, and"
    echo "      this stops. Until then, legacy behaviour is preserved exactly."
  else
    PINDIR="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-build-config-pin.XXXXXX")"
    if materialize_pin "$PIN" "$PINDIR"; then
      BASE="$PINDIR"
      BASE_DESC="FS-GG/.github@${PIN:0:12} dist/dotnet/ (this repo's pin)"

      # Is the receiver behind? Answer it ONLY with real history. Every receiver's CI clones .github at
      # depth 1 and materialize_pin then fetches the pinned commit as a SECOND, disconnected shallow
      # root — so ancestry and commit counts are literally unanswerable there. Asking anyway would
      # print a confident lie ("your pin is not an ancestor of main!") about every correctly-pinned
      # repo in the org, on every run. A gate that cries wolf by construction is one nobody reads.
      head_sha="$(git -C "$GITROOT" rev-parse HEAD 2>/dev/null || true)"
      shallow="$(git -C "$GITROOT" rev-parse --is-shallow-repository 2>/dev/null || echo true)"
      if [[ -n "$head_sha" && "$PIN" != "$head_sha" ]]; then
        BEHIND="the pin is not the .github commit this check is running from (${head_sha:0:12})"
        if [[ "$shallow" == "false" ]]; then
          n="$(git -C "$GITROOT" rev-list --count "$PIN..$head_sha" -- dist/dotnet scripts/sync-build-config.sh 2>/dev/null || true)"
          [[ -z "$n" || "$n" == "0" ]] || BEHIND="$n build-config change(s) behind ${head_sha:0:12}"
        fi
      fi
    else
      # FAIL OPEN, AND MEAN IT.
      #
      # We know this repo INTENDS to be pinned — it committed a pin — but we cannot read the baseline it
      # names. So we cannot tell "behind" from "hand-edited", and the two have opposite verdicts. The
      # only honest thing is to REFUSE TO JUDGE: warn loudly, and do not fail.
      #
      # This branch used to just fall back to comparing against main, and that was WRONG in the precise
      # way this whole item is about. A receiver that is merely BEHIND does not match main — so the
      # fallback RED-LIT it. One transient `git fetch` blip in CI was therefore enough to re-create the
      # merge freeze (#499/#536) on an innocent PR that changed nothing, on a REQUIRED check, while the
      # comment above it claimed to be failing open. A gate that reddens people for a network hiccup is
      # the defect wearing a new hat.
      #
      # The residual, stated plainly: a receiver that BOTH corrupts its pin AND hand-edits a managed file
      # goes green. That is accepted. It requires a deliberate, conspicuous edit to a file whose only
      # content is a bot-written SHA — visible in any review — and the propagate bot rewrites both halves
      # on its next run, after which the check bites again. Weigh that against the alternative, which is
      # freezing four repos because GitHub's git endpoint blinked.
      PIN_UNRESOLVED=1
      echo "WARN: $PIN_REL pins FS-GG/.github@${PIN:0:12}, which could not be resolved (unknown commit," >&2
      echo "      unfetchable after 3 tries, or a managed file did not exist at it)." >&2
      echo "      This check CANNOT be evaluated: without that baseline there is no way to tell a repo" >&2
      echo "      that is merely BEHIND from one with a HAND-EDITED file, and those have opposite" >&2
      echo "      verdicts. Refusing to guess — reporting ADVISORY and passing (.github#592)." >&2
      echo "      Re-sync to rewrite the pin; the propagate bot will also do it on its next run." >&2
      # Drop OUR scratch dir by name. Never glob the tmp prefix: a parallel run of this script owns a
      # sibling directory matching the same pattern, and a bulk rm would delete a baseline out from
      # under it mid-diff.
      rm -rf "$PINDIR"; PINDIR=""
    fi
  fi
fi

drift=0
for rel in "${FILES[@]}"; do
  # apply/adopt DISTRIBUTE from $SRC — a sync writes what .github ships now, always.
  # check COMPARES against $BASE — the receiver's pinned commit when it has one, else $SRC.
  # These are different questions and they must not collapse back into one variable: diffing against
  # $SRC in the check arm is precisely the race #592 removed.
  src="$SRC/$rel"
  base="$BASE/$rel"
  dst="$TARGET/$rel"

  case "$mode" in
    check)
      if [[ ! -f "$dst" ]]; then
        echo "DRIFT (missing): $rel"; drift=1
      elif ! diff -q "$base" "$dst" >/dev/null 2>&1; then
        echo "DRIFT (differs): $rel"; drift=1
      else
        echo "ok: $rel"
      fi
      ;;
    apply|adopt)
      mkdir -p "$(dirname "$dst")"
      # First-time adoption: a pre-existing, hand-authored .props is renamed to *.local.props
      # (the canonical file imports it), so repo-specific settings survive the takeover.
      if [[ "$rel" == *.props && -f "$dst" ]] && ! grep -q "$MARKER" "$dst"; then
        local_dst="${dst%.props}.local.props"
        if [[ "$mode" == "adopt" ]]; then
          if [[ -e "$local_dst" ]]; then
            # Both a hand-authored $rel and a pre-existing *.local.props exist. We cannot move the
            # hand-authored file onto *.local.props (that target is taken), and falling through to the
            # canonical `cp` below would silently destroy the hand-authored file's settings. Refuse this
            # file — fail-closed, exactly like apply-mode's refusal below (.github#126, review M1). The
            # operator merges the wanted settings from $rel into the existing *.local.props, deletes
            # $rel, then re-runs --adopt.
            echo "REFUSING to adopt $rel: a hand-authored $rel and $(basename "$local_dst") both exist." >&2
            echo "  Adopting would overwrite the hand-authored $rel, but its content cannot be moved to" >&2
            echo "  $(basename "$local_dst") (already present). Merge the settings you want from $rel into" >&2
            echo "  $(basename "$local_dst"), then delete $rel and re-run --adopt." >&2
            drift=1
            continue
          else
            mv "$dst" "$local_dst"
            echo "adopted: $rel -> $(basename "$local_dst")"
          fi
        else
          echo "REFUSING to overwrite hand-authored $rel (no '$MARKER' marker)." >&2
          echo "  Run with --adopt once to move it to $(basename "$local_dst"), or remove it first." >&2
          drift=1
          continue
        fi
      fi
      # The tool manifest is fully managed and, unlike the .props, has NO repo-local override to
      # import into — it is distributed verbatim and OVERWRITTEN on every re-sync, which is the
      # documented update path (docs/build/README.md). Being JSON it cannot carry the $MARKER comment
      # the .props use to tell a managed file from a hand-authored one, so apply cannot refuse a
      # hand-authored manifest without also refusing a legitimately stale managed one and breaking
      # re-sync. So it must not be *silently* clobbered (.github#387): on a first-time --adopt, save
      # the existing manifest to *.local.json (a plain backup — the manifest has no include, so it is
      # NOT re-imported) before writing canonical; on a plain apply re-sync, at least WARN before an
      # overwrite that changes content. A byte-identical manifest is a quiet no-op in either mode.
      if [[ "$rel" != *.props && -f "$dst" ]] && ! diff -q "$src" "$dst" >/dev/null 2>&1; then
        local_dst="${dst%.json}.local.json"
        if [[ "$mode" == "adopt" ]]; then
          if [[ -e "$local_dst" ]]; then
            # A hand-authored $rel and a pre-existing *.local.json both exist: we cannot back $rel up
            # onto a taken target, and overwriting would lose it. Refuse — fail-closed, as the .props
            # adopt does above (.github#126).
            echo "REFUSING to adopt $rel: a hand-authored $rel and $(basename "$local_dst") both exist." >&2
            echo "  Back up or merge the tools you want from $rel, delete $(basename "$local_dst"), then re-run --adopt." >&2
            drift=1
            continue
          fi
          mv "$dst" "$local_dst"
          echo "adopted: $rel -> $(basename "$local_dst") (backup only; the manifest has no import — merge any custom tools back by hand)"
        else
          echo "WARNING: overwriting $rel, which differs from the managed source. The tool manifest is" >&2
          echo "  fully managed and has no *.local override; its previous content is replaced. If it held" >&2
          echo "  repo-specific tools, run --adopt instead (keeps a *.local.json backup) or recover from git." >&2
        fi
      fi
      cp "$src" "$dst"
      echo "wrote: $rel"
      ;;
  esac
done

# ---- Record the provenance pin (.github#592) -----------------------------------------------------
#
# ONLY on a fully clean sync. `drift` is non-zero here when a file was REFUSED (a hand-authored .props,
# or an adopt whose *.local target was taken), and in that case the receiver's managed files are NOT
# the content of any single .github commit. A pin written over that state would be a false claim, and
# --check would then measure a half-synced repo against a baseline it never matched — turning a loud,
# actionable refusal into a silent, permanent red. So the pin is earned by a complete sync, or not
# written at all.
#
# A missing source_sha (no git, a tarball, an exported tree) is NOT fatal: the files still sync and the
# receiver stays in legacy mode. Refusing to distribute over an unknowable pin would be an outage we
# inflicted on ourselves for a provenance note.
if [[ "$mode" != "check" && "$drift" -eq 0 ]]; then
  pin_sha="$(source_sha)"
  if [[ "$pin_sha" =~ ^[0-9a-f]{40}$ ]]; then
    write_pin "$TARGET" "$pin_sha"
  else
    echo "WARN: no $PIN_REL written — the FS-GG/.github commit being distributed is not knowable here." >&2
    echo "  Either this is not a git checkout, or dist/dotnet/ has UNCOMMITTED changes (so the files just" >&2
    echo "  written match no commit, and any pin would be a false claim). The sync itself is complete and" >&2
    echo "  correct; the receiver simply stays on the pre-#592 compare-against-main drift check." >&2
    echo "  Commit dist/dotnet/, or pass FSGG_BUILD_CONFIG_SHA=<sha>, to pin it." >&2
  fi
fi

# UNEVALUATABLE, NOT FAILED (.github#592). The pin named a baseline we could not read, so the DRIFT
# lines above were produced against main — which a merely-BEHIND receiver never matches. Reporting them
# as a failure would red-light an innocent PR on a required check, which is the freeze this item exists
# to end. Print them as ADVISORY and pass; the pin, once resolvable again, gives a real verdict.
if [[ "$mode" == "check" && -n "$PIN_UNRESOLVED" ]]; then
  if [[ "$drift" -ne 0 ]]; then
    echo ""
    echo "ADVISORY (not a failure): the file(s) above differ from .github as currently checked out."
    echo "  Because $PIN_REL could not be resolved, this is NOT evidence of a hand-edit — a repo that is"
    echo "  simply BEHIND the org baseline differs from it in exactly this way, and that is green"
    echo "  (.github#592). Re-sync to rewrite the pin and get a real verdict."
  fi
  echo "Done ($mode; pin unresolved — check reported ADVISORY, see WARN above)."
  exit 0
fi

if [[ "$mode" == "check" && "$drift" -ne 0 ]]; then
  # Name a runnable command, not a relative path that exists only in .github (#633). Both forms are
  # printed because there are two readers of this failure and they are standing in different places:
  # someone with a .github checkout (the resolved path works), and someone looking at a red gate in a
  # receiver repo, who has no .github checkout at all (the clone form works).
  #
  # %q, not bare interpolation: these lines exist to be PASTED. An unquoted path with a space in it
  # would paste as two arguments and run the sync against the wrong directory. %q leaves an ordinary
  # path untouched, so this costs nothing in the normal case.
  #
  # The clone form clones into a FRESH mktemp dir rather than a fixed /tmp/... path, because a fixed
  # one makes the command fail on its SECOND use ("destination path already exists and is not an empty
  # directory") — for the reader with two drifted repos, or who simply runs it twice. A remediation
  # that only works once is the same defect as one that never works.
  {
    echo ""
    echo "Build-config drift detected: the file(s) marked DRIFT above differ from $BASE_DESC."
    if [[ -n "$PIN" && "$BASE" != "$SRC" ]]; then
      # The pinned reading of a red gate, and it is a genuinely different accusation from the old one.
      # Pre-#592 a red here often meant "somebody else changed .github and you have not caught up yet" —
      # not your fault, not fixable from your branch. It cannot mean that any more: the baseline IS the
      # commit this repo says its files came from. So a difference is a LOCAL EDIT, every time, and the
      # author reading this can fix it from the branch they are already on. Say so, or they will go
      # looking for the upstream change that is no longer there.
      echo ""
      echo "This repo is PINNED to FS-GG/.github@${PIN:0:12}, and the files above do not match what that"
      echo "commit distributes. So this is NOT 'you are behind main' — being behind main is green now"
      echo "(.github#592). A managed file has been HAND-EDITED in this repo. Re-sync to restore it, and"
      echo "put any repo-specific settings in Directory.Build.local.props / Directory.Packages.local.props,"
      echo "which the sync never touches."
    fi
    echo ""
    echo "This script lives in FS-GG/.github and is NOT checked into the repo being checked, so there is"
    echo "no 'scripts/sync-build-config.sh' in $TARGET. Re-sync with whichever fits where you are:"
    echo ""
    echo "  # ...from a checkout of FS-GG/.github (this script, resolved):"
    echo "  $(printf '%q' "$SELF") $(printf '%q' "$TARGET")"
    echo ""
    echo "  # ...from the root of the repo that just went red, with no .github checkout to hand:"
    echo "  d=\$(mktemp -d) && git clone --depth 1 https://github.com/FS-GG/.github.git \"\$d/org\" \\"
    echo "    && \"\$d/org/scripts/sync-build-config.sh\" ."
    echo ""
    echo "Then commit the updated file(s). Do not hand-edit them — they are overwritten on every re-sync."
    echo ""
    echo "If that re-sync REFUSES a file, this repo is ADOPTING one it hand-authored before the org"
    echo "managed it. Re-run the same command once with --adopt, which moves your version aside to"
    echo "*.local.props (still imported, so your settings survive) before writing the canonical file."
  } >&2
  exit 1
fi
if [[ "$mode" != "check" && "$drift" -ne 0 ]]; then
  exit 1
fi

# GREEN, BUT BEHIND — and that is a NOTICE, not a failure (.github#592).
#
# This is the case the whole item turns on. The receiver's files faithfully match the commit it is
# pinned to; .github has simply moved on since. Nobody did anything wrong, nothing in this PR can or
# should fix it, and failing here would re-create the merge freeze the pin exists to end. The bot
# (#626) already has a rolling, auto-merging PR open to close the gap.
#
# Deliberately NOT bounded ("fail if more than N commits / N days behind"). A staleness bound would
# reintroduce a failure that depends on time and on .github's commit rate rather than on this repo's
# tree — which is the exact class of defect being removed. Currency is the bot's job to enforce, and
# this line's job is to make the gap visible while it does it.
if [[ "$mode" == "check" && -n "$BEHIND" ]]; then
  echo ""
  echo "NOTICE: build config is coherent with its pin, and BEHIND the org baseline ($BEHIND)."
  echo "        This is GREEN on purpose. Being behind is not a defect in this PR and is not yours to"
  echo "        fix here: build-config-propagate (.github#626) keeps a rolling, auto-merging sync PR"
  echo "        open in this repo that bumps the managed files and $PIN_REL together."
fi
echo "Done ($mode)."
