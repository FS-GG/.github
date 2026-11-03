# LEARN card classification and execution provenance

Source candidate for Delivery #5273, under Outcome #5248. Native activation requires a published,
qualified installed caller and independent native readback. A source build does not establish adoption.

## Fields and compatibility

The maintained `board-v2 migration-prepare`, `migration-apply` and `migration-readback` path accepts the
optional closed `learn` extension, `fs.gg.board-v2.learn-annotations/1`. Its prepared and recovery readback reports use version3. The maintained
[`board_v2_native_results.py`](../../scripts/board_v2_native_results.py) consumer decodes that closed
LEARN scope and checks selected identities, field/assignment shapes and original effect preimages;
version1/2 reports retain their prior contracts. Actual CLI output is round-tripped through that
consumer and its complete field-transition projection in the source qualification tests.
It creates only these additive fields:

| Field | Native type | Meaning |
| --- | --- | --- |
| Hardness | Single select | Low, Medium, High, Unknown, under `learn-02-category-v1` |
| Actual models | Text | Provider observed or launch confirmed model by role; evidence level is explicit |
| Actual effort | Text | Provider observed or launch confirmed effort by role; missing facts remain Unknown |
| Execution provenance | Text | Durable owning-issue evidence comment |

Hardness uses the [LEARN-02 rubric](../roadmaps/learn-02-delivery-classification-and-routing.md): High
when H≥13 or D3/D5≥2; Medium when H≥7; otherwise Low. Any missing dimension yields Unknown. Safety
floors influence routing independently: a low hardness Delivery may still require best/high routing.
Original scores, evidence, rubric, source revision, requested profiles and overrides remain in the
issue's immutable classification record. A requested model or effort never supplies an actual value.

The existing binding5/schema3 reader verifies its selected required fields and tolerates these
additional fields. No schema successor, re-enrollment or binding rewrite is needed merely to add
annotations. Qualification must still establish that the actual installed migration caller supports
the extension. New field/option IDs come from independent readback; a same-name field with missing,
conflicting or unselected identity refuses creation/adoption. Existing fields, options, enrollment,
planning values and automation remain unchanged.

A LEARN scope selects no `initialPlanning` or Work level changes. It adds no membership and never
unarchives a card. It may update only the four annotation fields on an exact existing issue card.
Original dispatch snapshots stay immutable. The current card category may advance on an explicit new
revision-bound assessment, with its own durable issue record and exact old native preimage. Unknown
never erases an existing known category. Execution summaries use a JSON array of role/value/evidence-level strings, so model names containing
separators remain unambiguous. An update must preserve every previously known role/value fact;
incomplete cohorts or role-prefixed Unknown values refuse instead of erasing history. Unknown role
may become a known role, and launch-confirmed evidence may become provider-observed for the same
model/effort. Provider-observed confidence cannot regress to launch-confirmed confidence under a
partial later cohort. Evidence URLs must match the exact selected owning repository/issue and numeric comment
identity. Execution summaries and provenance can advance as cumulative observations arrive. Missing execution
values never erase an existing known summary. Every change remains under the existing shared writer
lock, exact prepared bytes, source/package identity, adjacent native preimage, bounded planning window,
possible-send custody and independent native readback. LEARN scalar writes reuse the existing item-scoped groups and final
whole-group retention check, including exact replacement preimages, so backfill does not reread the
whole project for every individual field. There is no provider compare-and-swap;
cooperating writers must retain the existing owner-controlled window and uncertain sends stay held.

## Maintained dispatch and completion path

The coordinator's [observation lifecycle](../../.agents/skills/work-board/references/observation.md)
invokes this path at initial dispatch and terminal completion. It creates no timer, persistent queue,
second scheduling authority or generic GraphQL writer.

1. At initial dispatch, freeze the original issue body and prepare the existing dispatch snapshot with
   `scripts/learn-dispatch.py --input INPUT --output INTENT`. The snapshot stores the category/version
   alongside dimension scores. Record it with the existing separately authorized `--record` path.
2. After `finish` and bounded `usage-reconcile`, prepare an evidence cohort with
   `scripts/learn-projection.py --input COHORT --output NEW_PRIVATE_DIRECTORY`. The projector reads only
   original dispatch snapshots, original attributed launch briefs and observed provider facts. It emits
   `projection.json`, durable issue-comment bodies and its own coverage counts with `effects: 0`.
3. Select that Delivery's emitted record as `COMPLETION`. Prepare it with
   `scripts/learn-dispatch.py --input CURRENT_NATIVE_PREIMAGE --completion-input COMPLETION --output INTENT`.
   The current body is only the recording preimage; it supplies no historical hardness features. Record
   with the same `--record` command and custody root. Completion comments use `fsgg:learn-completion/v1`;
   the existing `learn-task-snapshot` telemetry fact joins their exact full JSON by digest.
4. At either recording boundary, pass `--projection-template TEMPLATE --projection-scope-output NEW_SCOPE`
   to `--record`. After exact native comment readback and retained telemetry submission, this writes
   one private annotation-only migration scope using the read-back comment URL. The template contains
   the actual installed source/artifact/principal joins, independently read-back field IDs and selected
   native issue identity. It has empty `views` and `initialPlanning`, and no Work level extension.
5. Verify the telemetry receipt, then use the qualified installed engine's `board-v2 migration-prepare
   --scope-file NEW_SCOPE --report-file NEW_PREPARED`. Review the exact delta and apply it through
   `migration-apply` with the existing exact prepared SHA256, accountable owner, selection reference and
   bounded planning window. The lifecycle owner verifies native readback before completing this metadata
   effect. A held annotation effect is reported once with its owner and reopening condition.

The recording path retains its shared per-Delivery lock, fsynced possible-send custody and full-byte
batch validation. Lost comment/telemetry responses never authorize an automatic resend. A retained
record can prepare the identical projection scope again without reposting or overwriting different scope
bytes. Separate metadata effects retain their original identities after partial success.

## Historical cohort and evidence

`fsgg.learn-projection-input/1` requires `complete: true`, an explicit native Delivery population
(`itemId`, `nodeId`, `state`, `archived`, `workLevel`) and three envelope lists: `classifications`,
`launches`, `observations`. Each envelope carries `payload`, its canonical JSON SHA256 and a retrieval
`reference`. The operating owner verifies the reference's authority and acquisition completeness;
a supplied digest proves byte identity, not authenticity or complete historical capture.

Current open Deliveries can supply separate `assessments` envelopes created by `assess_scope` from a
fresh owning-issue body/revision. They score normative Done when (or the explicitly declared current
body when no section exists), retain capturedAt/input digest/rubric/basis, and never enter pre-dispatch
calibration. Original retained dispatch snapshots take precedence unless an explicit reviewed new assessment
sets `supersedesClassificationDigest` to that exact retained classification digest. That advance
changes the card current category while retaining `originalClassification` for historical calibration.
A replacement with a mismatched digest refuses; original absence is still reported.

Classification payloads are original `fsgg.learn-dispatch/1` snapshots; retrospective current bodies,
branch names and model selections cannot substitute. Launch payloads retain `itemId`, `invocationId`
and the original `brief` beginning with the exact `fsgg-dispatch` marker. A retained original Root-recorded accepted collaboration launch observation may establish launch-confirmed model/effort
configuration, bound by its canonical returned agent identity and original snapshot digest. Cards show
that useful configuration with `[launch-confirmed]` when provider-effective facts are missing; the
record keeps provider-effective Unknown separate. The original observation must attest an accepted
running/completed launch, never an unexecuted routing proposal. Missing role stays Unknown. A Root-recorded launch confirmation is labelled with that provenance in
the issue record; an unavailable raw returned tool response remains an explicit verification gap. It
does not establish provider-resolved model, effective effort, or an exact child-turn usage join.

Provider observations retain
original `provider-usage-observation/1` payloads: the `threadId` joins the original invocation, and only
its observed `model` and `effort` supply actual values. Conflicting assignment/observation identities and any thread shared by distinct invocation identities
or roles refuse the projection rather than cross-attributing provider facts. Exact duplicates cannot multiply observations. Every observed model/effort pair,
worker/critic role and execution identity remains in the issue record, including changes within one
invocation. Unknown effort remains distinct from a requested high effort.

Coverage describes the complete **selected metadata population**, independently of dispatch enrollment.
Include every closed Delivery in that qualified selection, including unenrolled ones. Report Outcomes,
missing Work level, non-issue cards and archived history unavailable to the selection as separate scope
gaps; do not silently omit them or claim a whole-board historical census. An archived row gets a durable
issue record, while its native card update remains held unless a separately supported route exists.
Closed items without an original snapshot or attributed provider facts get explicit Unknown values and
attributed gaps. The projector always reports complete historical execution coverage as Unknown:
complete native membership cannot prove that every past runtime was captured.

## Visible fields and activation

Use a separate explicit migration scope to create/read back the four fields, then select the exact
existing views and append the desired LEARN field IDs to their current `visibleFieldIds`. The writer
preserves the existing column prefix, layout, filter and name. Its native transition check preserves
all grouping, vertical grouping and sorting bytes as well as unrelated views and values. It writes
supported `UpdateProjectV2ViewInput.configuration.visibleFieldIds`; it does not recreate views or infer
unsupported grouping/sorting mutations.

Activation verifies hardness in both existing board layouts (Programme Board and Deliveries), plus the
selected Completed and All items views, using independent native visible-field readback and visible
card inspection. Preserve their IDs and existing display configuration. Record every actual installed,
native, visible-field and backfill result on #5273. Review, source tests or a merge alone cannot close
the Delivery's native adoption promise.
