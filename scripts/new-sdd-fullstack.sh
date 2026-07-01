#!/usr/bin/env bash
# new-sdd-fullstack — create a full-stack FS.GG product using only existing, published
# machinery, with NO FS.GG.Templates checkout required (unlike Templates' new-fullstack.sh).
#
# It orchestrates the commands that already exist today:
#   1. fetch the newest rendering provider descriptor from FS.GG.Templates (curl, no clone)
#   2. fsgg-sdd scaffold        (SDD lifecycle skeleton + runnable Rendering app)
#   3. governance overlay       (dotnet new fs-gg-governance — default on, non-blocking)
#   4. fsgg-sdd doctor          (read-only: confirm the product is coherent with its set)
#
# Currency stays EXPLICIT (per ADR-0009): fetching `main` gives the current coherent set;
# pass --ref <tag> to pin a reproducible version; run --upgrade to reconcile a behind project.
#
# Usage:
#   new-sdd-fullstack <target-dir> <product-name> [--ref <git-ref>] [--upgrade] [--no-governance]
#     --ref <git-ref>    FS.GG.Templates ref to fetch the descriptor from (default: main = newest)
#     --upgrade          also run `fsgg-sdd upgrade` after scaffolding (reconcile if behind)
#     --no-governance    skip the governance overlay
set -euo pipefail

TARGET="${1:?target dir required}"
PRODUCT="${2:?product name required}"
shift 2

REF="main"
DO_UPGRADE="no"
GOVERNANCE="yes"
while [ $# -gt 0 ]; do
  case "$1" in
    --ref)           REF="${2:?--ref needs a value}"; shift 2 ;;
    --upgrade)       DO_UPGRADE="yes"; shift ;;
    --no-governance) GOVERNANCE="no"; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

DESCRIPTOR_URL="https://raw.githubusercontent.com/FS-GG/FS.GG.Templates/${REF}/providers/rendering.providers.yml"

# 1. Fetch the newest rendering provider descriptor (no clone). The committed descriptor is
#    concretely pinned, so it is directly usable — no placeholder substitution needed.
echo "==> fetching provider descriptor from FS.GG.Templates@${REF}"
mkdir -p "$TARGET/.fsgg"
curl -fsSL "$DESCRIPTOR_URL" -o "$TARGET/.fsgg/providers.yml"
echo -n "    pinned: "; grep -m1 'source:' "$TARGET/.fsgg/providers.yml" | sed 's/^[[:space:]]*//' || true

# 2. Scaffold via existing fsgg-sdd machinery.
echo "==> fsgg-sdd scaffold"
fsgg-sdd scaffold --root "$TARGET" --provider rendering --param "productName=$PRODUCT"

# 3. Governance overlay (default on, advisory/non-blocking) — best-effort: needs the published
#    FS.GG.Templates template package on a reachable feed. Applied after scaffold so it is not
#    flagged writing into the SDD-owned .fsgg/ tree.
if [ "$GOVERNANCE" = "yes" ]; then
  echo "==> adding governance overlay (default: light / non-blocking)"
  if dotnet new install FS.GG.Templates >/dev/null 2>&1; then
    dotnet new fs-gg-governance -o "$TARGET" --appName "$PRODUCT" --defaultProfile light \
      || echo "!!  governance overlay failed — the product is fine without it."
  else
    echo "!!  could not install the FS.GG.Templates template (feed not reachable?). Skipping."
    echo "    add later:  dotnet new install FS.GG.Templates && dotnet new fs-gg-governance -o $TARGET --appName $PRODUCT"
  fi
fi

# 4. Confirm coherence via existing machinery (read-only).
echo "==> fsgg-sdd doctor"
fsgg-sdd doctor --root "$TARGET" || true

# 5. Optional explicit reconcile (ADR-0009: never automatic).
if [ "$DO_UPGRADE" = "yes" ]; then
  echo "==> fsgg-sdd upgrade"
  fsgg-sdd upgrade --root "$TARGET"
fi

echo
echo "Done: full-stack product in $TARGET"
echo "Next:  cd $TARGET && dotnet build && dotnet run     # then: fsgg-sdd charter"
