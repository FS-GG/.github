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
acceptance bind the initial and immediately preceding review. All records in one generation bind the
same critic.

If a PR head moves after acceptance, the accepted generation remains immutable and the next contiguous
revision may start a new `initial` generation at the new head. A new generation is legal only immediately
after acceptance and only when its head differs; its critic and confirmation rounds start fresh. The live
reader retires accepted older-head generations and parses only the generation effective at the current
head. This is an append-only recovery, not a rewrite or deletion.

The adapter projects a validated v2 ledger into the existing pure review state machine. Any malformed or
tampered v2 record blocks; it is never ignored in favor of a passing v1 marker.

### Authoring review records

Create a JSON draft with all semantic fields shown below. Set `revision` to `0`, `previousDigest` to
`null`, and `digest` to the empty string; the live writer reads the complete paginated PR comment
ledger, refuses malformed or stale history, assigns the next revision and exact predecessor, computes
the digest, validates the resulting chain, and posts the canonical v2 JSON comment:

```bash
scripts/fsgg-coord review record FS.GG.Repo#42 review-draft.json --pr 77 --json
```

```json
{
  "schema": "fsgg.coord.review-decision/v2",
  "subject": "FS-GG/FS.GG.Repo#42/pr/77",
  "revision": 0,
  "previousDigest": null,
  "headSha": "0123456789abcdef0123456789abcdef01234567",
  "critic": "minted-critic-identity",
  "verdict": "pass",
  "acceptedExceptions": [],
  "routeApplicability": "not-meaningful",
  "routeEvidence": ["bounded reason this review has no meaningful runtime route comparison"],
  "policyVersion": "structured-decisions/1",
  "kind": "initial",
  "round": 0,
  "initialReview": null,
  "precedingReview": null,
  "timestamp": "2026-08-14T12:00:00Z",
  "digest": ""
}
```

For `confirmation`, use a positive `round`, `pass` or `changes-required`, and bind both
`initialReview` and `precedingReview` to the exact GitHub comment URLs returned by the preceding writer
calls. The writer compares both values with the actual structured comments and refuses a mismatch before
posting. For `acceptance`, use verdict
`accepted`, bind both URLs, and keep `acceptedExceptions` empty; exceptions accepted by the critic
belong on the initial or confirmation record. Every acceptance is preflighted through the live generation
retirement and terminal-chain parser; successful output reports `effectiveChainValidated: true`. The
command rejects legacy v1 input before any write.

## New-only authority

M6 removed v1 body hashes, locator/subsequence matching, evidence classifications, and prose review-marker
authority. Every active route has a validated structured record; the complete active/inert census records
zero active legacy-only route or review authority. The 76 inert historical comments remain untouched, but
the runtime cannot interpret them as authorization. Missing or invalid v2 evidence fails closed.

Never rewrite or delete a structured record to repair it; append the next correctly linked revision.
Rollback is a reviewed higher-version code release and preserves the structured ledgers—it does not restore
the retired prose authority.
