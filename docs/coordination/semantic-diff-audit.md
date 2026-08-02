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

When the critic marks `diff-audit-required: true`, host acceptance must carry the exact receipt fact:
`diff-audit-receipt: complete`, a non-empty base SHA, `diff-audit-head` equal to `accepted-head`, and
`diff-audit-disposition: all-resolved`. The typed review-chain parser rejects any missing or stale fact.
