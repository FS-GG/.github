#!/usr/bin/env bash
# Sparse-checkout closure ledger rendering for repos-audit.sh.
#
# The audit's sparse reader writes records from command substitutions, so this seam owns the one
# place that turns that ledger into operator output and the counters consumed by the caller's exit
# protocol. Keeping it here lets sparse-specific reporting evolve without serializing on the audit's
# other receiver checks.

repos_audit_sparse_report() { # <ledger> <read repos> <rostered repos> <unread repos>
  local ledger="$1" read_repos="$2" rostered_repos="$3" unread_repos="$4"
  sparse_count() { grep -cE "^$1"$'\t' "$ledger" 2>/dev/null || true; }

  sparse_findings="$(sparse_count finding)"
  sparse_refusals="$(sparse_count refusal)"
  sparse_workflows="$(sparse_count workflow)"
  sparse_unparseable="$(sparse_count unparseable)"
  # shellcheck disable=SC2034 # consumed by repos-audit.sh after this sourced seam returns.
  sparse_noverdict="$(sparse_count noverdict)"
  local sp_cross sp_graded sp_patterns sp_clones sp_ungraded sp_rule4 sp_rule4_subjects
  IFS=' ' read -r sp_cross sp_graded sp_patterns sp_clones sp_ungraded sp_rule4 sp_rule4_subjects <<< "$(
    awk -F'\t' '$1 == "counts" { c += $2; g += $3; p += $4; f += $5; u += $6; r += $7; s += $8 }
                END { printf "%d %d %d %d %d %d %d", c, g, p, f, u, r, s }' "$ledger")"

  local kind message
  while IFS=$'\t' read -r kind message; do
    case "$kind" in
      finding)     echo "::error::repos-audit: sparse-checkout closure — $message" ;;
      refusal)     echo "::error::repos-audit: sparse-checkout closure REFUSED a shape it cannot grade — $message" ;;
      noverdict)   echo "::error::repos-audit: sparse-checkout closure could NOT read a rostered repository's git tree, so rule (4) did not run — $message" ;;
      ungraded)    echo "  UNGRADED $message" ;;
      unresolved)  echo "  note $message" ;;
      unparseable) echo "  note $message: this workflow would not parse, so GitHub cannot run it and it cannot fetch anything — not graded" ;;
    esac
  done < "$ledger"

  if [ "$sp_cross" -eq 0 ]; then
    echo "repos-audit: sparse-checkout closure (#1529) — read $sparse_workflows workflow(s) across $read_repos of $rostered_repos rostered repo(s) ($unread_repos NOT audited, $sparse_unparseable unparseable) and found NO cross-repo \`actions/checkout\` at all. NOTHING was asserted about this class; that is not a clean bill."
  else
    echo "repos-audit: sparse-checkout closure (#1529) — $sp_patterns sparse pattern(s) over $sp_graded of $sp_cross cross-repo checkout(s) fully graded, in $sparse_workflows workflow(s) across $read_repos of $rostered_repos rostered repo(s) ($unread_repos NOT audited, $sparse_unparseable unparseable); $sp_clones full clone(s) not graded; $sp_ungraded step(s) UNGRADED; $sparse_findings finding(s), $sparse_refusals refusal(s)."
    echo "repos-audit: sparse-checkout closure — rule (4), do the named directories exist?, ran for $sp_rule4 of $sp_rule4_subjects graded cross-repo step(s): the tree this audit holds, plus every ROSTERED repository whose git tree the API served (#1556). Every step it could not run for is named above with the reason; in cone mode that leaves the step UNGRADED, never ok."
  fi
}
