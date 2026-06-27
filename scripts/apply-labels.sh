#!/usr/bin/env bash
# Apply the shared cross-repo coordination labels to every FS-GG repo.
# GitHub has no org-level labels, so they are created per-repo (idempotent via --force).
set -euo pipefail

REPOS=(
  FS-GG/FS.GG.SDD
  FS-GG/FS.GG.Rendering
  FS-GG/FS.GG.Governance
  FS-GG/FS.GG.Templates
  FS-GG/.github
)

# name|color|description
LABELS=(
  "cross-repo|1d76db|Touches more than one FS-GG repo"
  "cross-repo:request|0e8a16|Incoming request from another repo"
  "cross-repo:response|5319e7|Response/handoff back to another repo"
  "blocked|b60205|Blocked on another repo"
  "contract-change|d93f0b|Changes a versioned cross-repo contract (update the registry)"
)

for repo in "${REPOS[@]}"; do
  for entry in "${LABELS[@]}"; do
    name="${entry%%|*}"; tmp="${entry#*|}"; color="${tmp%%|*}"; desc="${tmp#*|}"
    gh label create "$name" --repo "$repo" --color "$color" --description "$desc" --force >/dev/null
    echo "  $repo  $name"
  done
done
echo "Done."
