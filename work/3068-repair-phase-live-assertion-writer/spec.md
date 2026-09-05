---
schemaVersion: 1
workId: 3068-repair-phase-live-assertion-writer
title: Repair Phase Live Assertion Writer
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Repair Phase Live Assertion Writer Specification

Prose status: specified

## User Value
Review hosts can durably authorize an accountable same-head repair assertion through the typed live CLI.

## Scope
- SB-001: Live review inspect, wait-enter, and review-record wiring plus lifecycle and end-to-end coverage; no compatibility event or no-op commit authority.

## Non-Goals
- SB-002: Do not change ordinary or repair-phase ceilings, authorize no-op commits, or add compatibility explicit-event authority.
- SB-003: Do not alter or merge the parked GS2-03.7 implementation while this blocker is being fixed.

## User Stories
- US-001 (P1): As a review host, I can append an accountable repair assertion and receive the next live protocol command without manufacturing a tree change.
- US-002 (P1): As a critic or landing host, I can re-read the assertion from durable PR authority and verify every binding before advancing the chain.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an unchanged repaired head after a changes-required review, when an accountable third party writes the typed assertion, then live inspect emits the repair-confirmation wait command and wait-enter succeeds.
- AC-002 [US-001] [FR-002]: Given validated ordinary exhaustion, a fresh claim and PR, and an initial changes-required record on the already-repaired head, when the assertion and wait are present, then a kind=repair-phase record is reachable and a fresh successor pass can reach host acceptance.
- AC-003 [US-002] [FR-003]: Given a stale, wrong review/head/PR/purpose, implementer/current-critic, malformed, old-schema, or unrelated assertion record, when the live reader evaluates it, then that record is non-authorizing audit noise and cannot poison current facts or a later valid grant. Exact physical duplicates collapse; independently eligible grantors remain distinct valid grants.
- AC-004 [US-002] [FR-004]: Given an item, an unrelated closed/unmerged historical PR, and an open exact-head current PR whose completed pass requests host acceptance, when live review state is inspected, then current authority selects host acceptance without reading the irrelevant PR ledger.
- AC-005 [US-002] [FR-004]: Given a transition that requires repair-entry provenance and a separately numbered exhausted predecessor PR, when live review state is inspected, then the item timeline and selected predecessor ledger produce the exact purpose-bearing next command; malformed evidence on the current PR or selected exhausted predecessor fails closed without assuming item/PR number equality.
- AC-006 [US-002] [FR-005]: Given `review assert-repair`, when the current claim, current PR lifecycle/head/ledger, or `ResumeImplementer` transition is absent or unreadable, then the command refuses before any predecessor read, assertion append, or wait mutation.
- AC-007 [US-002] [FR-005]: Given the exact immutable changes-required decision, when the host runs `review host-grant REF --pr N`, then admission requires independently `opts.Worker=None`, resolved provenance `FromEnv "FSGG_WORKER"`, and canonical resolved `Worker.Id` satisfying Kernel `Identity.isMintedWorkerId`. Raw environment spellings that `slug` to the same canonical minted id are accepted equivalents. Lexical resolved shape is enforced; freshness/uniqueness/non-spoofing remain cooperative.
- AC-008 [US-002] [FR-005]: Given a valid exact host receipt owned by the invoking host, when that host runs `review assert-repair REF --pr N`, then it resolves the same env-minted identity, derives live implementer/purpose/round/predecessor separately, revalidates host ≠ current implementer/critic, selects only its own grant, and appends its independently exact assertion. Legacy reviews without an actual backward-linked receipt park or require a fresh review.
- AC-009 [US-002] [FR-005]: Given multiple independent eligible hosts, repeated producers, delayed visibility, lost POST response, or duplicate posts, duplicates collapse per `(AnsweredDecisionKey, grantedBy, env-minted/v1)` while distinct hosts coexist. Closed v1 makes same-key valid bytes/digest deterministic; wrong digest/canonical bytes are invalid noise and cannot revoke a valid grant. Each host may append its own assertion, wait eligibility remains existential, and one host becoming ineligible cannot poison another.
- AC-010 [US-002] [FR-005]: Given purpose/predecessor change, claim heartbeat, ordinary turnover or force, the immutable receipt identity is unchanged; current assertion consumption recomputes live eligibility and transition. A host equal to the current implementer is ineligible, while unrelated host grants remain eligible. Stale/wrong/malformed/edited/wrong-author records are independent noise, and neither writer mutates downstream state.
- AC-011 [US-002] [FR-005]: Given host grant, assertion, existing repair-confirmation wait, repair-phase successor review and acceptance fixtures, the black-box flow reaches host acceptance without schema migration downstream, and source mutation controls prove the host-grant/assertion commands themselves perform zero downstream writes.

## Functional Requirements
- FR-001: After ordinary exhaustion and an accountable assertion bound to the exact review, head, and disjoint grantor identity, the live oracle MUST make repair-confirmation wait and kind=repair-phase record reachable without changing the candidate head. (Stories: US-001; Acceptance: AC-001)
- FR-002: The production review path MUST carry the durable assertion through inspect, wait-enter, and review-record so exhaustion through repair-phase successor pass and host acceptance is executable. (Stories: US-001; Acceptance: AC-002)
- FR-003: The writer and reader MUST classify stale, wrong-subject/head/review/purpose, self-granted, current-critic-granted, malformed, old-schema, and unrelated assertion records independently as non-authorizing audit noise. Invalid records MUST NOT poison exact current facts or any valid grant. Exact duplicate physical posts collapse; multiple independently eligible exact grants form a monotone set, and wait eligibility is existential over that set. Comment order, latest-wins, absence, malformed noise, and caller-selected values MUST NOT select authority. (Stories: US-002; Acceptance: AC-003)
- FR-004: The CLI contract, live oracle, and live writer MUST derive the exact authorized purpose and next command from the same live state, including all required bindings; caller syntax MUST NOT accept or select purpose. Derivation MUST first establish whether the current transition requires exhausted-predecessor authority, MUST keep current-PR authority fail-closed, and only then resolve a unique selected exhausted predecessor from typed item cross-references and its review ledger. A predecessor ledger is marker-bearing only when the existing codec recognizes exactly one canonical unquoted marker at the start of the comment body; substring, quote, code-fence, indentation, or later-line occurrences MUST NOT select authority. Irrelevant historical PRs and invalid assertion noise MUST NOT poison an exact-head current transition, while malformed evidence on the selected current/exhausted predecessor MUST fail closed. (Stories: US-002; Acceptance: AC-004, AC-005)
- FR-005: Add append-only `fsgg.coord.review-host-grant/v1` over immutable `AnsweredDecisionKey`, `grantedBy`, `env-minted/v1`, and deterministic canonical digest. Kernel `Identity` MUST export `isMintedWorkerId` beside `mint`, reusing the private word table and accepting an already-canonical resolved id exactly `<known-word>-<four lowercase hex>`. Commands apply it to `Worker.Id` after normal `slug`; raw `FSGG_WORKER` spellings that normalize to the same canonical id are intentionally equivalent and are not raw-string validated. Canonical resolved ids not matching the grammar—including `root`, `host`, `system`, role labels, unknown words and wrong suffixes—are non-worker for these commands. Producer admission MUST be the conjunction of parsed `opts.Worker=None`, `Identity.resolve None` provenance exactly `FromEnv "FSGG_WORKER"`, and minted canonical id. `opts.Worker.IsSome` refuses before identity use/comment mutation even though `resolve None` would still return an admissible env identity. Reachable after-command flags are tested; before-command flags are pinned as actual parser unknown/non-routable for both engine and wrapper. Every session/shared/absent/non-minted source refuses. Freshness/uniqueness/non-spoofing remains cooperative. (covers AC-006, AC-007, AC-008, AC-009, AC-010, AC-011)

  Canonical digest preimage is the compact semantic JSON object in schema-declared property order, encoded UTF-8 without BOM, with the shared codec's deterministic JSON string escaping, no insignificant whitespace and no trailing LF. The posted body is the exact ASCII marker, one LF, the compact full JSON object including lowercase 64-hex digest, and one terminal LF. CRLF, reordered/extra/duplicate properties, alternative escape spellings, or missing/extra terminal bytes are noncanonical. `answeredDecisionBodySha256` separately hashes the decoded answered-comment body scalar's exact UTF-8 bytes as returned, preserving its existing code points and newline bytes.

  Parser evidence is contract data, not a model constant. Candidate engine and wrapper both return rc=1/`unknown command: --worker` for `--worker vole-418 whoami`, and rc=0 with `FromFlag` for `whoami --worker vole-418`; new host commands MUST preserve the before-command nonroute and explicitly refuse the after-command parsed option with zero receipt/assertion comments.

  The host receipt producer MUST inspect the current open/unmerged PR, exact decision and live implementer before appending, and MUST write no assertion or downstream record. `review assert-repair REF --pr N` is also host-owned, resolves the same env-minted identity, selects only that identity's logical grant, separately derives live transition, and appends that host's assertion. Valid same-key v1 receipts are necessarily byte/digest identical and collapse; wrong digest, noncanonical bytes, malformed/extra/duplicate fields, edited/foreign author and stale/wrong fields are independently invalid noise and MUST NOT revoke an existing valid grant/assertion. Distinct hosts coexist and reduce existentially. Purpose/predecessor/heartbeat/turnover do not change receipt identity; live eligibility/exactness are recomputed on consumption. Existing downstream schemas remain unchanged.

## Ambiguities
- AMB-001 resolved by DEC-001: Ordinary review authority lacks host identity, so use the explicitly authorized additive `review-host-grant/v1` receipt. Its producer uses the actual `Identity.resolve None` runtime contract with distinguishing provenance under the cooperative non-spoofing boundary; it does not claim cryptographic host-role authentication.

## Public Or Tool-Facing Impact
- Add public host-owned `review host-grant REF --pr N [--json]`, the append-only `review-host-grant/v1` receipt, and host-owned `review assert-repair REF --pr N [--json]`. No downstream schema changes. Revisions 7–17 remain rejected or corrective evidence.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 3068-repair-phase-live-assertion-writer`.
