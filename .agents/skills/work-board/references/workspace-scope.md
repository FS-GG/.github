# Workspace board scope

Limit scheduling, claims, worktrees, and reports to the current product workspace. Use the workspace's
coordination configuration and repository instructions; do not silently fall back to the org board.
Touch-sets are compared inside this repo. Cross-repo findings become requests through
cross-repo-coordination rather than edits in another checkout.

For a selected V2 product, read `.fsgg/board-v2-binding.json` and the `productBoard` provenance.
Binding version 3 selects exactly one owner-local repository and one to five native issues.
A view filter is not a repository write boundary. Use the canonical read-only `board-v2 inspect`
route in work-board; an organization Binding version 2 never authorizes product execution.
Keep Status/Track/Roadmap and human Blocked values untouched; only separately authorized
Observation refresh is supported. Unreadable configuration or missing published tooling refuses
the selected effect, while local-only source development remains supported.
