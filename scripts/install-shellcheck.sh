#!/usr/bin/env bash
#
# install-shellcheck.sh — acquire the pinned, checksum-verified shellcheck binary WITHOUT a transport
# failure being able to produce a `lint` verdict (.github#2501).
#
# THE DEFECT THIS CLOSES. shell-lint's `lint` job used to `curl` the pinned tarball directly on every
# run, on the critical path of the verdict. Four `503`s from GitHub's release CDN reddened PR #2479 —
# a PR touching only `Directory.Build.props` and a `.fsproj`, no shell whatsoever — and `.github#2433`
# makes `lint` a REQUIRED status context on `main`. A required context that cannot report deadlocks
# every merge with no bypass (the failure shape `.github#1519`/`.github#574`/`.github#549` exist to
# catch). This script is the "cache restore ... with the existing checksum verification retained" half
# of that item's remedy: the CALLER (`.github/workflows/shell-lint.yml`) restores `--cache-dir` from
# `actions/cache` before invoking this script, so a warm cache makes acquisition ZERO-NETWORK. This
# script's OWN job is narrower and does not know about `actions/cache` at all: given a cache directory
# that may or may not already hold a checksum-valid tarball, make the verified binary available, and
# fail in a way a caller can tell apart from "shellcheck ran and found something" (#266's distinction,
# one layer below the lint verdict itself: "I could not acquire the tool" is not "I checked, and it's
# clean" any more than "I could not run the tool" is, in scripts/lint-shell.sh's own exit-code table).
#
# THE CHECKSUM IS OF THE TARBALL, ALWAYS VERIFIED, REGARDLESS OF SOURCE. A cache restore is trusted no
# more than a fresh download: both paths run through the identical `verify_checksum`, so a corrupted or
# tampered cache entry is caught exactly like a corrupted or tampered download would be — never
# silently trusted because it "came from our own cache". A checksum mismatch is never treated as "try
# the next source and hope" for THIS artifact; it is discarded and the next URL (if any) is tried fresh
# — but nothing is ever installed except a tarball whose sha256 equals the pin. See the acceptance
# criteria on .github#2501: (1) an acquisition failure must be distinguishable from a lint finding,
# (2) checksum verification is retained and a mismatch fails closed, (3) the acquisition path must be
# exercisable with the network source unavailable and still reach a real verdict — this is exactly what
# a warm CACHE_DIR proves: see tests/shell-lint/run.sh's install-shellcheck legs.
#
# Usage:   install-shellcheck.sh <version> <sha256> <cache-dir>
# Prints:  the verified binary's path, on stdout, on success.
# Env:     SHELLCHECK_URLS   space-separated candidate URLs to try in order (default: the single
#                            upstream GitHub Releases URL for <version> — the exact source #2501's own
#                            instances 503'd on). Overridable so a future mirror can be added without
#                            editing this script, and so tests can point it at a fake source instead of
#                            the network.
# Exit codes:
#   0  the pinned, checksum-verified binary is available at the printed path.
#   2  COULD NOT ACQUIRE it from any source, or the pin/arguments are malformed — a transport/usage
#      failure, never a claim about any shell file. Never 1: this script lints nothing, so it has no
#      "findings" exit to collide with scripts/lint-shell.sh's rc=1.
set -uo pipefail

usage() {
  echo "usage: install-shellcheck.sh <version> <sha256> <cache-dir>" >&2
}

VERSION="${1:-}"
SHA256="${2:-}"
CACHE_DIR="${3:-}"
if [ -z "$VERSION" ] || [ -z "$SHA256" ] || [ -z "$CACHE_DIR" ]; then
  usage
  exit 2
fi

mkdir -p "$CACHE_DIR" 2>/dev/null || { echo "::error title=install-shellcheck: cannot create cache dir::$CACHE_DIR" >&2; exit 2; }

TARBALL="$CACHE_DIR/shellcheck-v${VERSION}.linux.x86_64.tar.xz"
EXTRACT_DIR="$CACHE_DIR/extracted"
BIN="$EXTRACT_DIR/shellcheck-v${VERSION}/shellcheck"

# verify_checksum <path> — 0 if <path> exists and its sha256 equals the pin, 1 otherwise. No output:
# callers decide what a mismatch means (discard-and-continue on download, hard-fail on final check).
verify_checksum() {
  [ -f "$1" ] || return 1
  local sum
  sum="$(sha256sum "$1" 2>/dev/null | awk '{print $1}')" || return 1
  [ "$sum" = "$SHA256" ]
}

DEFAULT_URL="https://github.com/koalaman/shellcheck/releases/download/v${VERSION}/shellcheck-v${VERSION}.linux.x86_64.tar.xz"
# shellcheck disable=SC2206  # intentional word-splitting of a caller-supplied, space-separated list
URLS=(${SHELLCHECK_URLS:-$DEFAULT_URL})

acquired=0
if verify_checksum "$TARBALL"; then
  # CACHE HIT: the tarball already sitting in $CACHE_DIR already matches the pin. No `curl` is ever
  # invoked on this path — this is the exact property that turns a warm `actions/cache` restore into
  # zero-network acquisition, and what tests/shell-lint/run.sh proves by pre-seeding this file and
  # pointing SHELLCHECK_URLS at an unreachable host.
  acquired=1
else
  for url in "${URLS[@]}"; do
    tmp="$(mktemp "$CACHE_DIR/.download.XXXXXX" 2>/dev/null)" || { echo "::error title=install-shellcheck: cannot create temp file in $CACHE_DIR" >&2; exit 2; }
    if curl -fsSL --retry 3 --connect-timeout 10 -o "$tmp" "$url" 2>/dev/null; then
      if verify_checksum "$tmp"; then
        mv -f "$tmp" "$TARBALL"
        acquired=1
        break
      fi
      # Fetched SOMETHING, but it does not match the pin — never trust it, and never let a mismatch
      # from one source poison the attempt at the next: discard and keep going.
      echo "::warning title=install-shellcheck: checksum mismatch::downloaded from $url but its checksum does not match the pinned ${SHA256}; discarding and trying the next source." >&2
      rm -f "$tmp"
    else
      rm -f "$tmp"
      echo "::warning title=install-shellcheck: source unreachable::could not fetch $url" >&2
    fi
  done
fi

if [ "$acquired" -ne 1 ]; then
  echo "::error title=install-shellcheck: COULD NOT ACQUIRE shellcheck::every candidate source failed (tried: ${URLS[*]}). This is a transport failure, not a lint finding — no shell file has been read." >&2
  exit 2
fi

if [ ! -x "$BIN" ]; then
  mkdir -p "$EXTRACT_DIR" 2>/dev/null || { echo "::error title=install-shellcheck: cannot create extract dir::$EXTRACT_DIR" >&2; exit 2; }
  if ! tar -xJf "$TARBALL" -C "$EXTRACT_DIR" 2>/dev/null; then
    echo "::error title=install-shellcheck: extraction failed::the checksum-verified tarball at $TARBALL could not be extracted." >&2
    exit 2
  fi
fi

if [ ! -x "$BIN" ]; then
  echo "::error title=install-shellcheck: binary missing after extraction::expected $BIN" >&2
  exit 2
fi

printf '%s\n' "$BIN"
