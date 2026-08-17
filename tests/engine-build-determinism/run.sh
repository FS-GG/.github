#!/usr/bin/env bash
# Prove scripts/check-engine-build-determinism.py can be RED, and on what (.github#2688).
#
# A gate that has never been red is equally consistent with "nothing was ever wrong" and "it cannot
# fire", and reading it cannot separate those. So this harness inverts it by breaking its SUBJECT —
# the packed bytes — and not merely its predicate, and it also asks the one question a byte-scanning
# gate can otherwise answer confidently and wrongly: what does it do when it grades NOTHING?
#
#   arm 1  the production shape: pack as the repository is configured. Expect exit 0.
#   arm 2  SUBJECT MUTATION: pack the same tree with DeterministicSourcePaths=false, which is exactly
#          the state `main` was in before .github#2688. Expect a non-zero exit that names every one
#          of the eight packed entries as embedding the build root. This is the leg that shows the gate
#          measures the artifact rather than the project files: nothing in the tree changes between
#          arm 1 and arm 2 except the property MSBuild resolves.
#   arm 3  NON-VACUITY: hand the gate a package with none of the four coord assemblies in it. "Found
#          no leak" and "graded no entries" must not share an exit code. Expect a non-zero exit.
#
# Arms 1 and 2 each pack from a clean obj/ and bin/ — the checker does that itself, and must, because
# MSBuild's CoreCompile incrementality is keyed on timestamps rather than on the property values that
# produce the compiler command line.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
gate="$root/scripts/check-engine-build-determinism.py"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

# ── arm 1 — the production shape is green ────────────────────────────────────────────────────────
echo "== arm 1: the repository's own configuration must pass"
if ! python3 "$gate" --repo "$root" >"$work/arm1.out" 2>"$work/arm1.err"; then
  cat "$work/arm1.out" "$work/arm1.err" >&2
  fail "the gate reds on the repository's own packed output; the property is not reaching the build"
fi
grep -q "mappedRootOccurrences=1" "$work/arm1.out" \
  || fail "arm 1 passed without observing a mapped source root — the gate graded something unexpected"
echo "   ok"

# ── arm 2 — break the subject, and require every entry to be named ───────────────────────────────
echo "== arm 2: DeterministicSourcePaths=false must be refused, entry by entry"
if python3 "$gate" --repo "$root" --property DeterministicSourcePaths=false \
     >"$work/arm2.out" 2>"$work/arm2.err"; then
  cat "$work/arm2.out" >&2
  fail "the gate PASSED a package built without DeterministicSourcePaths — it cannot fire"
fi
for entry in FS.GG.Coord.Core.dll FS.GG.Coord.Core.pdb \
             FS.GG.Coord.GitHub.dll FS.GG.Coord.GitHub.pdb \
             FS.GG.Coord.Cli.Kernel.dll FS.GG.Coord.Cli.Kernel.pdb \
             fsgg-coord-engine.dll fsgg-coord-engine.pdb; do
  grep -q "embeds the build root" "$work/arm2.err" \
    || fail "arm 2 red for the wrong reason; expected a build-root finding"
  grep -q "$entry" "$work/arm2.err" \
    || fail "arm 2 did not name $entry — the gate is not grading every packed coord entry"
done
echo "   ok — all eight packed coord entries named"

# ── arm 3 — a package it cannot grade is a refusal, not a pass ───────────────────────────────────
echo "== arm 3: a package carrying none of the coord assemblies must be refused"
python3 - "$work/empty.nupkg" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w") as z:
    z.writestr("FS.GG.Coord.Cli.nuspec", "<package/>")
    z.writestr("tools/net10.0/any/FSharp.Core.dll", b"not a coord assembly")
PY
if python3 "$gate" --repo "$root" --package "$work/empty.nupkg" \
     >"$work/arm3.out" 2>"$work/arm3.err"; then
  fail "the gate PASSED a package it graded nothing in — 'found nothing' and 'looked at nothing' share an exit code"
fi
grep -q "graded nothing" "$work/arm3.err" \
  || fail "arm 3 red for the wrong reason; expected the empty-selection refusal"
echo "   ok"

echo "engine-build-determinism: 3/3 arms as expected"
