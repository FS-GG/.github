# Semantic-diff audit

For a declared bulk rename, the worker records the deterministic `SemanticDiff` inventory from the
exact base and head SHA. The inventory is evidence, not an intent oracle: an accountable author chooses
one disposition for every occurrence (`intended-contract-change`, `intended-test-doc-update`,
`generated-output`, or `accidental-fix-required`). `unresolved`, an unknown schema, duplicated ids,
or either SHA changing is a refusal.

The inventory deliberately includes comments, quoted and escaped/interpolated F# strings, character
literals, serialized keys, golden text, tests, documentation, and generated artifacts. A rename that
only changes an identifier is a control and produces no text occurrence. This is the reusable lesson
from Rogue3 feedback: compilation cannot prove that protocol or example text was intentionally changed.

Routing is mechanical: `FSGG_DIFF_AUDIT_THRESHOLD` (default 5 occurrences), a `[bulk-rename]` or
`Bulk rename: true` head-commit declaration, or a standalone `Bulk rename: true` line in the captured
live item body makes the receipt required. The item body is a typed command input, not remembered caller
environment state. Use `-` in the optional receipt position when producing the initial inventory with an
item body: `diff-audit BASE HEAD OLD NEW - item-body.md --paths ...`.
The initial command emits the complete occurrence-level JSON and exits red while required occurrences
remain unresolved. Passing that JSON back after accountable dispositions completes it; a missing,
duplicate, malformed, path-mismatched, or old-head receipt is refused.

Host acceptance carries that concrete JSON as base64 in `diff-audit-receipt-v1`, not independently
typed summary claims. The host reads the live PR base/head blobs, recomputes the inventory, and the typed
review-chain parser requires exact paths, tokens, stable occurrence identities, and every disposition.
The live driver independently reads the item body, immutable head commit, and occurrence threshold
facts; `diff-audit-required: false` is rejected when any of those facts requires the audit.

## Omitting the receipt does not answer the threshold question

The threshold counts OCCURRENCES, so it is always measured in occurrences — including on the path where
no receipt was submitted. That path used to substitute the changed-FILE count, which is a different
quantity and always the smaller one: a one-file rename with six quoted occurrences supplied `1`, fell
under the default threshold of 5, computed `mechanically required = false`, and let a
`diff-audit-required: false` chain merge with no receipt at all. Omitting the receipt therefore answered
the very question the receipt exists to answer (.github#2144).

So when no receipt supplies the rename tokens, the engine recovers them from the live diff itself:
`SemanticDiff.discoverRenames` pairs each removed line with an added line that differs by exactly one
consistent word-boundary substitution, and `discoveredOccurrences` inventories every occurrence those
tokens account for. Line numbers are irrelevant — pairing is by content, so unrelated insertions and
deletions cannot hide a rename.

Evidence that cannot be read stays UNKNOWN and requires the receipt. A blob the API refuses is not
`0` occurrences; only a `404` — the server stating the path is absent at that ref, i.e. a file this PR
added or deleted — is read as empty content. An empty changed-file list from a successful `prFiles` read
is the one shape where zero is an honest measurement, because there is no diff for a rename to hide in.
A missing fact is never converted into a negative one.
