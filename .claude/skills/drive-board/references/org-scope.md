# Org-wide board scope

The ledger is the FS-GG Coordination Projects v2 board and the scope spans all rostered repositories.
Allocate workers per repository so each worktree and package policy remains local, while dependencies
and contract-release ordering remain visible to the host. Use cross-repo coordination for contract
edges and coherent releases. Final reporting groups landed items and remaining blockers by repository.

**A board row whose repo is not in the roster is unreachable, not absent.** `batch`/`take` are
repo-directed and resolve their `--repo` argument against `registry/repos.yml`; a row on the board for
an unrostered repository answers `WARNING — no board row names repo <id>` and can never be admitted to
a lane, however it is classed or ranked. Measured 2026-08-04: `EHotwagner/rogue3#96` sat `Ready`,
`Class: defect`, severity `High`, and `EHotwagner/S.I.R.#138` `Blocked`, while `batch --repo rogue3`
listed only the nine rostered ids. That is invisible to the stopping test, which counts startable
defects — so read the whole board with `ready --json`, compare the repos it names against the roster,
and **report every row outside it by number** rather than letting a repo-directed sweep imply it was
covered. Rostering such a repo is an ownership decision: surface it, do not infer it.
