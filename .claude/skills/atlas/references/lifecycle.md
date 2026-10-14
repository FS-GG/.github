# Knowledge lifecycle

Before substantial work, search Atlas and retrieve relevant stable IDs. Verify immutable
citations for implementation/decisions and refresh operational facts against their live owner.
An installed seed is historical knowledge, not evidence of current deployment or authority.

At delivery, compare the result with the retrieved knowledge. Capture a concise reusable
finding when it adds knowledge; explicitly supersede incorrect or changed same-owner
records, or retain conflicting accounts with their evidence. At normal change completion,
update affected knowledge or explain briefly why the existing records still cover the result;
an unchanged record needs no new version. Keep source and accepted ADRs/plans canonical.
Run the archive validation and
normal owner delivery checks; a local capture is not a protected or installed update.
If capture cannot land in the authorized scope, hand off the stable ID or prepared finding,
the exact gap, and its owner. Do not claim knowledge is current from a draft.

At handoff, reference delivered Atlas IDs instead of copying their history. Keep private
attempt receipts, unresolved effects and admission constraints in their existing custody;
Atlas is not an operational log. On recovery, retrieve those IDs from the current available
archive, check supersession/conflicts, and reconcile citations and live facts before action.
A missing record or archive is an explicit knowledge gap, not permission to invent evidence.

A stop request still stops work immediately. Include already captured IDs and outstanding
capture gaps in the save; do not start a new investigation or capture delivery after shutdown.

Persistent memory stores only repository/Atlas-ID pointers and instructions to retrieve and
verify them. Do not store copied facts, current versions/heads, grants, credentials or private
operational receipts there. Remove or replace stale factual memory with pointers when
encountered through supported memory mechanisms; never edit an internal memory database.

## Installed workspace guidance

When adopting the owner driver payload, check its policy projection with
`python3 .agents/skills/atlas/scripts/atlas.py guidance --root . --check` from the
repository root. When authorized to update owner guidance, omit `--check` to refresh
only the marked Atlas blocks; other agent instructions are preserved. A missing policy
is an adoption gap, not permission to invent one. Source delivery of this helper/policy
alone does not prove that an installed consumer ran it or adopted the archive update.
The installed `work-roadmap` entrypoint already routes through Atlas; staging policy is
transport. The generic SDD generator currently uses its separate product Knowledge API
and does not read `knowledgePolicy` or call this helper. Automatic root activation needs
a supported owner/provider hook and installed readback; preserve the generic API.
