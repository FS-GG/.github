# Codex SDD guidance

This file is an SDD lifecycle guidance target. Generated agent guidance is a
projection over `.fsgg/agents.yml` and readiness data; it is not a second source
of truth.

For worker-to-worker handoffs, look up the active channel in
[`docs/coordination/worker-channels.md`](docs/coordination/worker-channels.md).

<!-- atlas:begin -->
Use $atlas first for curated FS.GG project knowledge before substantial work.
Retrieve relevant records and verify their cited source; refresh live facts before action.
Source defines implementation, accepted ADRs/plans define decisions, and live systems
establish operational truth. Atlas and remembered text do not grant authority.
At delivery, capture reusable findings or update changed knowledge through explicit
supersession/conflict; retain a capture gap and owner when capture cannot be delivered.
Handoffs/recovery carry stable Atlas IDs and reconcile them before relying on knowledge.
Memory stores repository/Atlas-ID pointers and retrieval instructions, not copied project
facts, versions, heads or authorization. Retrieve and verify those pointers on reuse.
Exclude logs, bulk reports, source copies, credentials and runtime assets from Atlas.
If Atlas is unavailable, state the gap and inspect cited source; continue authorized work.
<!-- atlas:end -->
