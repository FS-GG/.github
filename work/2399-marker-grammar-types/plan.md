---
schemaVersion: 1
workId: 2399-marker-grammar-types
title: Independent-review marker grammar types
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2399-marker-grammar-types/spec.md
sourceClarifications: work/2399-marker-grammar-types/clarifications.md
sourceChecklist: work/2399-marker-grammar-types/checklist.md
publicOrToolFacingImpact: true
---

# Independent-review marker grammar types Plan

Prose status: planned

## Source Snapshot
- spec: work/2399-marker-grammar-types/spec.md sha256:1700e8f3429c651d55e72b58eb66a3c6063f3fa315a338f05a710c23bc7bf71a schemaVersion:1
- clarifications: work/2399-marker-grammar-types/clarifications.md sha256:19c44699f3d2aa4965096376e172caca22c4c4ff0a65df5c9fa3903e2dd3094f schemaVersion:1
- checklist: work/2399-marker-grammar-types/checklist.md sha256:3701974a200ed7477d34a5f2026ca0901750acc3d53442692668f0913e48d290 schemaVersion:1

## Plan Scope
- Work item 2399-marker-grammar-types is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `MarkerId`, `MarkerAnchor` (`LeadingLine | LeadingBlock |
  AnywhereInBody`), `MarkerOccurrence` (`Live of raw:string | Quoted | Competing of raw:string`), and
  `Marker = { Id: MarkerId; Anchor: MarkerAnchor }` to `Protocol.fsi`/`Protocol.fs`, plus a total
  `occurrences: knownMarkerTexts: string list -> marker: Marker -> body: string -> MarkerOccurrence list`
  that implements the LeadingBlock quoted-vs-competing rule (#2221/#2248) and the LeadingLine rule
  (#83758ec3/#d86ded2a) as one function. `ReviewPolicyDoc.QuotedMarkerRule: string` is deleted; a new
  `ReviewPolicyDoc.MarkerAnchors: Marker list` field (one entry per existing marker-name field) replaces
  it, and a private `renderLeadingBlockRule: MarkerAnchor -> string` dispatches over the anchor union to
  reproduce the exact prose `d58577ec` projected, so `generate-projections` (out of declared `Paths:`,
  left untouched) keeps reading an unchanged `reviewPolicy.quotedMarkerRule` JSON string.
- PD-002 [AC-001] [FR-001] complete: Widen (`scripts/fsgg-coord widen .github#2399`, verdict `disjoint`)
  onto `src/FS.GG.Coord.Cli/Snapshot.fs` and its test file. Deleting `QuotedMarkerRule` from
  `ReviewPolicyDoc` does not compile against `Snapshot.fs:942`
  (`w.WriteString("quotedMarkerRule", policy.QuotedMarkerRule)`), which is outside the item's original
  declared `Paths:` — a hard compile dependency, not optional scope growth. `Snapshot.fs` is changed to
  call the new rendering function instead of reading the deleted field, and to additionally emit
  `reviewPolicy.markerAnchors` (id + anchor per marker) and a new `reviewPolicy.markerFieldGrammar`
  array (closing #2369's gap: the `critic`/`reviewed-head`/`verdict` field names per marker, sourced
  from a new `Protocol.markerFieldGrammar` value that mirrors `Driver.fs`'s private
  `markerFieldGrammar` function, pinned against `Driver.parseReviewComments`'s own enforcement in
  `ProtocolTests.fs` rather than against `Driver.fs`'s private internals). These are ADDITIVE JSON keys;
  every byte `facts --json` emitted before this change is emitted unchanged after it — the "protocol
  bytes did not move" requirement is read as "no previously-emitted byte or generated skill-doc byte
  changed", not "the JSON is textually frozen", since the #2369 acceptance criterion explicitly requires
  new content to appear in `facts --json`. Evidence: an actual `facts --json` before/after diff captured
  in the PR body.
- PD-003 [AC-001] [FR-001] complete: `.github#2392` (delivery-route receipt binds to the WHOLE issue
  body) is not fixed here — its file is not in `Paths:` — but the PR states plainly whether
  `MarkerAnchor`/`occurrences` would make that fix a one-liner (it would not: #2392's defect is about
  the SUBJECT a receipt hashes, not about marker-occurrence classification within a body; the two are
  orthogonal, and the PR says so explicitly rather than leaving it implied).
- PD-004 [AC-001] [FR-001] complete: `AnywhereInBody` is declared and given a real `occurrences`
  branch (whole-body canonical-line scan, competing on >1) so the union is total and testable, but no
  marker in this item's family actually uses it — none of the five review markers or the field-grammar
  entries need it, so no per-marker justification comment is owed under the acceptance criterion's own
  wording ("a marker matched against a whole body compiles only if it declares AnywhereInBody" — none
  do).

## Contract Impact
- PC-001 [PD-001] [PD-002] wireFormat: `Protocol.fs` is `scripts/check-engine-freshness.py`'s
  `WIRE_SURFACE`; this PR's `facts --json` before/after diff is the evidence the change is additive-only
  to that surface, not a breaking rewrite, even though the release-freshness gate still correctly flags
  the file as touched.
- PC-002 [PD-002] command report: `fsgg-coord facts --json`'s `reviewPolicy` object gains
  `markerAnchors` and `markerFieldGrammar`; existing keys (`initialMarker` .. `quotedMarkerRule`) are
  unchanged in name and value.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `ProtocolTests.fs` tests `occurrences` directly for all three
  `MarkerAnchor` cases (Live/Quoted/Competing), including a LeadingLine reconstruction of the
  #83758ec3/#d86ded2a defect shape (marker line, blank line, prose — previously failed a whole-body
  match) and the existing LeadingBlock fenced-quote/competing cases pinned against
  `Driver.parseReviewComments`'s own behaviour, so the tests fail if `occurrences`' anchor handling is
  ever widened.
- VO-002 [PD-002] [PC-002] semanticTest: `SnapshotTests.fs` covers the new `markerAnchors`/
  `markerFieldGrammar` JSON emission; `dotnet build`/`dotnet test` across `Core` and `Cli` test projects
  gate the change before task generation.
- VO-003 [PD-001] [PC-001] diffEvidence: an actual `fsgg-coord facts --json` run before and after the
  change, diffed, and pasted into the PR body — proving no existing key/value moved.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas
  diagnose before write. No migration is owed: `ReviewPolicyDoc` is an internal engine type with no
  persisted on-disk instances, and the JSON change is additive.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: readiness/2399-marker-grammar-types/work-model.json and this
  work item's readiness views refresh from `spec.md`/`clarifications.md`/`checklist.md`/`plan.md` after
  each `fsgg-sdd` stage command; the implementation's own generated view is `fsgg-coord facts --json`,
  refreshed by running it after the code change and captured as the before/after diff evidence in VO-003.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2399-marker-grammar-types`.
