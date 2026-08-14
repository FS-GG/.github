# Structured route and review decisions

Machine authorization is append-only structured evidence. Issue and pull-request bodies are narrative:
editing narrative alone cannot grant, preserve, revoke, or widen authorization.

## Route record v2

Each `<!-- fsgg:route-decision/v2 -->` issue comment carries one JSON object with schema
`fsgg.coord.route-decision/v2`. It binds `subject`, positive contiguous `revision`, `previousDigest`,
`scope`, `dependencies`, `touchSet`, `policyVersion`, route selection, accountable agent and timestamp,
route rationale/bindings, and `digest`. The digest covers all structured inputs except itself. The first
revision has `previousDigest: null`; every later revision names the exact preceding digest.

The engine validates the whole observed v2 chain and consumes only its last record. A changed field,
missing/gapped revision, stale predecessor, malformed record, wrong subject, or wrong policy fails closed.
`delivery-route record` writes only this v2 form and refuses a v1 input.

## Review record v2

Each `<!-- fsgg:review-decision/v2 -->` pull-request comment carries schema
`fsgg.coord.review-decision/v2`. It binds the PR subject, contiguous decision revision, predecessor digest,
exact 40-hex head SHA, minted critic identity, `pass`/`changes-required`/`accepted` verdict, accepted
exception identifiers, runtime-route applicability evidence, policy version, record kind and round,
review back-references, timestamp, and digest. One ledger begins with `initial`; confirmations and host
acceptance bind the initial and immediately preceding review. All records bind the same critic.

The adapter projects a validated v2 ledger into the existing pure review state machine. Any malformed or
tampered v2 record blocks; it is never ignored in favor of a passing v1 marker.

## Migration and removal

Reads are dual during M4–M5: `legacy-only`, `structured-only`, `equivalent`, and `divergent` are explicit
classifications. Migration appends revision 1; it never edits or deletes an old comment. Active records
copy the explicit route, scope, dependency, and touch-set facts into v2, and every divergent classification
is reviewed before the v2 record becomes effective. New and revised decisions use only v2.

M6 may remove v1 body hashes, locator/subsequence matching, and prose review-marker authority only after
three consecutive operating cycles have zero unexplained dual-read difference, no active `legacy-only`
decision, and a rollback rehearsal proves the v2 ledger can be read from retained comments. Until that
explicit trigger, v1 is read-only compatibility—not an alternate write path.

Rollback before M6 is a code revert: append-only comments remain intact. Never rewrite or delete a v2
record to repair it; append the next correctly linked revision.
