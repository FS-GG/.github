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

The Atlas guidance body is authored once in `references/root-guidance.md`. The helper
reads that reference, and Drivers stages the identical bytes at `.fsgg/atlas-guidance.md`
through its existing workspace inventory. The optional `.fsgg/owner-guidance.json`
descriptor declares the `atlas` block for `claude` and `codex` and names its required
policy, skill, helper and reference files. It carries no command or execution hook.

A compatible SDD receiver can validate the selected owner payload and merge that body
between `<!-- atlas:begin -->` and `<!-- atlas:end -->` through its ordinary file effects.
Descriptor delivery alone does not prove activation: it requires the generic owner-guidance
implementation, publication and installed receiver readback. Older receivers retain their
existing behavior. The separate product Knowledge API and its historical records remain
under their own contract; `knowledgePolicy` by itself is not an activation mechanism.

From the receiving repository root, check manual/automatic agreement with
`python3 .agents/skills/atlas/scripts/atlas.py guidance --root . --check`. When authorized
to update owner guidance manually, omit `--check` to refresh only the marked Atlas blocks;
other agent instructions are preserved. A missing policy or reference is an adoption gap,
not permission to invent one. Automatic fresh scaffold and confirmed retained upgrade must
verify all declared prerequisites and preserve conflicting authored files; doctor remains
read-only. Claim those routes only after acceptance of the exact installed receiver.
