# Continue one already-owned legacy PR

The [#5269 Root decision](https://github.com/FS-GG/.github/issues/5269#issuecomment-6590105190)
permits a metadata continuation of one existing owner and PR without creating a reservation.
The maintained entry point is `tools/pr-lane-admission.py --repo OWNER/REPO
--recover-legacy-update plan.json --body-file body.md --title TITLE --inspect`.
Remove `--inspect` only for the concrete authorized operation, using the reviewed protected tool.
The policy decision authorizes implementation; it is not authorization for an individual write.

This route leaves admission metadata absent. Ordinary admission still counts the reservation and
refuses unknown identities or excess capacity. It does not create, close, retarget, merge, push source,
assign a campaign/chain, or initialize a delivery declaration. Existing ordinary admission, exact-two
adoption and the selected SC2 exception retain their contracts. Source qualification, normal CI,
integration and the owning Delivery's complete acceptance remain separate requirements.

## Closed plan and complete native population

The UTF-8 plan is a regular file of at most 64 KiB, with no duplicate or unknown JSON keys.
Its exact fields are:

| Field | Bound value |
| --- | --- |
| `schema` | `fsgg.legacy-pr-update/1` |
| `repo`, `repositoryId` | Canonical native full name and positive repository ID |
| `defaultBranch`, `defaultSha` | Current native default branch and exact lowercase 40-hex SHA |
| `number`, `previousHead` | Selected PR number and exact previous routine marker/source SHA |
| `authorization` | Exactly `commentId`, `nodeId`, `bodySha256` for the native Root operation comment |
| `successor` | `null` for the first operation; the explicit recovery object described below otherwise |
| `pulls` | The complete open roster, 1–20 entries, in increasing native number order |

Each roster row has exactly `number`, `id`, `nodeId`, `authorId`, `author`, `branch`, `headSha`,
`base`, `baseSha`, `bodySha256`, `titleSha256`, `delivery`, `issueId`, `issueBodySha256`.
Numbers and native IDs are positive integers. Commit IDs are lowercase 40-hex; text digests are
SHA256 of the exact UTF-8 native string. PR number, ID, node ID and head branch are unique.
`delivery` is a same-repository issue number, explicitly referenced by the PR using the maintained
Refs/closing-reference parser. The issue must remain open, declare `Work level: Delivery`, and match
its native ID and body hash. Other WIP PRs may lack routine markers; their actual reservations,
identities and text are preserved, without treating them as admitted or qualified.

The top-level default SHA and each observed PR base pointer are independent. A historical or
stacked PR base is guarded as observed, not replaced with current default. Full native PR detail
responses must agree with the complete paginated list; the list omits `merged` and
`maintainer_can_modify`, so those two fields come from detail. Every branch ref must match its row.
The shared repository lock covers the operation; the complete population, source, ownership and
Root declaration are re-read immediately before the one possible PATCH and during independent
readback. A missing, extra, duplicated or changed row refuses.

Only the selected PR must have an existing valid `source-change` routine marker, at `previousHead`
or its current `headSha`, and no lane-marker footprint. Native comparison must prove the previous
commit is an ancestor of the pushed current commit, or identical. The complete comparison is bounded
to 100 commits; a larger, missing or divergent comparison refuses. There is no local-history fallback.

The proposed body is also bounded to 64 KiB. It must retain the owning Delivery reference. The helper
preserves every existing machine marker, including the routine operation/extra fields and delivery
declaration. Only the routine head binding changes. Caller marker injection, lane assignment and
implicit continues/completes declarations refuse. The PATCH contains only body and title.

## Native Root declaration

The operation comment belongs to the selected owning Delivery. Its entire trimmed body is one
closed marker: `<!-- fsgg:legacy-pr-update/v1 JSON -->`. `JSON` is compact, sorted-key JSON with
exactly these fields:

| Field | Value |
| --- | --- |
| `repo`, `prId`, `number`, `delivery`, `branch` | The selected native repository, PR, owner issue and branch |
| `previousHead`, `currentHead` | The previous source/marker SHA and current pushed SHA |
| `operation` | `update-body-title` |
| `successor` | Exactly the plan's successor object or `null` |

`declaration()` in [the maintained helper](../../tools/legacy-pr-update.py) defines the canonical
bytes. The plan binds the exact comment ID, node ID and complete body hash. REST comment identity,
body and issue URL must join the GraphQL IssueComment node, database ID, body and repository/issue.
The author must belong to `scripts/board-currency-rules.py`'s maintained `rootLogins`. If edited,
GraphQL must establish a trusted Root editor; missing edit evidence refuses. The caller supplies
neither Root login lists nor ambient authority or expiry exceptions. A policy comment or local
record cannot substitute for this operation declaration.

## Custody, uncertainty and later operations

Private custody lives under the existing `pr-lane-known-roster` state directory, in a separate
`legacy-update-<repository/native-PR-ID hash>` directory. Ordered per-operation journals bind the
validated plan and proposed text hashes, native authorization, source comparison, complete before
population, ownership, and selected before/expected metadata hashes. Raw PR/issue prose is excluded.
The helper fsyncs `possible-send` before calling GitHub. Full independent selected metadata and
complete roster readback must match the expected one-row change before `complete` is recorded.

A send error, unavailable/mismatching readback or intervening change retains the original journal.
Re-entry with an existing journal is read-only and refuses resend, including under a changed plan,
filename, body or head. There is no automatic cleanup or reset. GitHub offers no PR-text
compare-and-swap; adjacent checks do not claim server atomicity. A readback storage failure also
requires reconciliation rather than a retry.

A genuinely later update requires a new Root operation comment and explicit successor object:
`custodySha256` (SHA256 of the exact prior journal file), `intentSha256` (the prior intent), and
`disposition` (`delivered` or `not-sent`). The same object is bound inside the Root declaration.
The prior comment ID cannot be reused. For `delivered`, current native body/title must match the
prior expected result and the next previous-head binding must equal the prior current head.
For `not-sent`, native body/title and previous head must match the prior preimage; a completed
journal cannot be declared not sent. This is explicit Root reconciliation, not an inferred retry
based on elapsed time or a failed acknowledgement. The prior journal remains byte-for-byte intact;
a successor gets the next numbered journal under the same stable PR identity.

## Acceptance evidence

The provider-boundary controls exercise the actual CLI and metadata builder, including six legacy
reservations, real list/detail differences, historical bases, Root author/editor checks, source
ancestry, injected metadata, native drift, storage failure, lost acknowledgements, incomplete
readback and explicit successor recovery. These controls do not establish a native operation.
Source review, ordinary required CI and protected integration precede the first actual qualification;
that operation still needs its adjacent native inputs, Root declaration and complete readback.

## Packaged dependency closure

Drivers includes the admission entry point and its `tools/legacy-pr-update.py` sibling through the
maintained workspace inventory. The package
projects the exact canonical rule bytes to `.fsgg/legacy-pr-root-rules.py` for this explicit mode. That
alias wins when present; otherwise a source checkout uses `scripts/board-currency-rules.py`. This
preserves the shared Root trust and reference parser without installing the ordinary Root-only board
gate into receiver workspaces. The actual stager records the delivered hashes. Source/staging controls
do not establish publication, installed adoption or authorization for a native continuation.

The inspection result, intent and existing custody record carry `rootRulesSource` with the selected
relative path and SHA256 of the exact executed rule bytes. Join that fingerprint to the selected
caller/package BOM. An alias left by an older installation is identified as that frozen source, not
silently reported as the current checkout policy. The fingerprint records provenance; it grants no
Root identity or operation authority by itself.
