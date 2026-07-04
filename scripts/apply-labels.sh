#!/usr/bin/env bash
# Apply the shared cross-repo coordination labels to every FS-GG repo.
# GitHub has no org-level labels, so they are created per-repo (idempotent via --force).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# The repo list is the org roster (registry/repos.yml) — every repo that `receives: labels`.
# ADR-0019: this was a hardcoded array duplicated across ~10 fabrics; it now reads the single
# authoritative roster, so adding/retiring a repo is one registry row, not a code edit here.
mapfile -t REPOS < <("$HERE/repos.sh" list --receives labels)
[ "${#REPOS[@]}" -ge 1 ] || { echo "apply-labels: roster returned no repos with 'labels'" >&2; exit 1; }

# name|color|description
LABELS=(
  "cross-repo|1d76db|Touches more than one FS-GG repo"
  "cross-repo:request|0e8a16|Incoming request from another repo"
  "cross-repo:response|5319e7|Response/handoff back to another repo"
  "blocked|b60205|Blocked on another repo"
  "contract-change|d93f0b|Changes a versioned cross-repo contract (update the registry)"
  "architecture-map:unaffected|c5def5|Opt out of the architecture-map reconcile gate (change does not alter the system shape)"
)

for repo in "${REPOS[@]}"; do
  for entry in "${LABELS[@]}"; do
    name="${entry%%|*}"; tmp="${entry#*|}"; color="${tmp%%|*}"; desc="${tmp#*|}"
    gh label create "$name" --repo "$repo" --color "$color" --description "$desc" --force >/dev/null
    echo "  $repo  $name"
  done
done
echo "Done."
