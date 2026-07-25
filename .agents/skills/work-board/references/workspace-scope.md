# Workspace board scope

Limit scheduling, claims, worktrees, and reports to the current product workspace. Use the workspace's
coordination configuration and repository instructions; do not silently fall back to the org board.
Touch-sets are compared inside this repo. Cross-repo findings become requests through
cross-repo-coordination rather than edits in another checkout.
