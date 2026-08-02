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
