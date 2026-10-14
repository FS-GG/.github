# Claude SDD guidance

This file is an SDD lifecycle guidance target. Generated agent guidance is a
projection over `.fsgg/agents.yml` and readiness data; it is not a second source
of truth.

For worker-to-worker handoffs, look up the active channel in
[`docs/coordination/worker-channels.md`](docs/coordination/worker-channels.md).

<!-- atlas:begin -->
Use $atlas as the first source for FS.GG project knowledge before substantial
investigation or implementation. Retrieve relevant records and follow their cited evidence.
Record reusable findings through Atlas before completing work; exclude logs, bulk reports,
source copies, credentials and runtime assets. Preserve superseded and conflicting findings.
Atlas owns curated knowledge; source defines implementation and live systems establish
current operational facts. Verify those facts before action. If Atlas is unavailable, state
the gap and use cited source directly; do not block otherwise authorized work.
<!-- atlas:end -->
