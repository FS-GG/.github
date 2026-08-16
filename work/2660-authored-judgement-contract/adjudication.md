# Adjudication of the fifteen headings deleted by `b84423e7`

Work id: `2660-authored-judgement-contract`. This file is the written record acceptance criterion 1 of
`.github#2660` asks for. It is referenced from `plan.md` PD-001.

## The test applied

`b84423e7` ("retire legacy decision authorities") reduced
`.claude/skills/pnext-item/references/independent-review.md` from 856 lines to 92 and deleted fifteen
headings. Its stated subject — retiring the v1 prose decision markers in favour of the structured
`fsgg.coord.review-decision/v2` ledger — was correct and is not revisited here.

Each deleted heading is adjudicated against one question, and only this one:

> Can `fsgg-coord` **seal** this fact, or is it a judgement a critic **authors**?

Content the engine seals is *decision authority*; the ledger now holds it and restoring the prose
would create a second, weaker source of truth. Content a critic authors is *judgement contract*; the
engine cannot hold it, nothing else gates it, and it left with the first kind because one file held
both. Line count is not the measure — several sections below are restored to a third of their length
and carry their whole contract.

`Verification:` `git show --stat b84423e7 -- .claude/skills/pnext-item/references/independent-review.md`
→ `764 +-------`; `git show b84423e7^:.claude/skills/pnext-item/references/independent-review.md | grep -n '^#'`
→ the sixteen heading lines (title plus the fifteen below).

## Dispositions

| # | deleted heading | disposition | reason |
|---|---|---|---|
| 1 | `## Runtime-route evidence gate` | **restore-condensed** | The ledger seals the *field shape* — four ordered evidence strings or one bounded reason — and the current file already states that. It cannot compute **when** a multi-route comparison is meaningful, nor that the critic must *execute* the comparison rather than read the two implementations and judge them equivalent. That is the whole content of the gate, and `pnext-item/SKILL.md` §5 still promises a critic "can execute or measure the comparison required by `independent-review`". Restored without the v1 marker templates. |
| 2 | `## Gate-inversion evidence` | **already-restored** | `.github#2551` restored it as six numbered steps. Not reopened. `Verification:` `grep -n '^## Gate-inversion evidence' .claude/skills/pnext-item/references/independent-review.md`. |
| 3 | `### The sub-shape a happy-path mutation does not catch` | **restore-condensed** | Split. Its vacuous-green half and its fixture-simpler / environment-richer table are carried by `.github#2551`'s steps 2, 5 and 6 and are not reopened. Its remaining half — **a non-answer reported as a confident answer**, and the instruction to invert the *unreadable input* as well as the happy path — was in neither that item's subject line nor its acceptance criteria, so it is part of the remainder this row owns. Restored as one clause and one new numbered step. |
| 4 | `### Worked example` | **do-not-restore** | Illustration, not contract. It demonstrates steps 5 and 7 against `FS.GG.Templates#379`. **Be precise about what the numbered steps carry, because the first draft of this row overstated it:** of the example's three generalised fixture properties, only the first — every fixture carries the decoys the real artifacts carry — is carried, by restored step 5. Properties 2 (detached and attached fixtures differ in exactly one line, so the pair is a controlled experiment) and 3 (every detach case is paired with a reattached control that must go green) appear nowhere in the restored contract, and this row does not claim they do. They stay out because they are fixture-construction **technique** for reaching step 5's obligation, not obligations the contract imposes on every review; step 5 binds the property, and a contract that also prescribed the method would be prescribing one measured incident's solution. The narrative survives in git and on that issue. |
| 5 | `### What the critic records` | **already-restored** | `.github#2551` restored it as the gate-inversion section's closing paragraph, including the `JUSTIFIED` / `DECORATIVE` / `NOT_MEASURED` vocabulary. Not reopened. |
| 6 | `## Handoff-assertion provenance` | **restore** | Pure authored judgement: every checkable assertion carries `Verification:`, and `unverified` is a first-class non-pejorative value rather than a missing field. No engine command reads a handoff. The rule binds the host relaying claims onward as much as the worker and critic who authored them, and nothing in the current file states it. |
| 7 | `### Issue and pull-request body evidence` | **restore** | ADR-0074 *depends* on this section existing: it places issue and PR bodies deliberately outside the static CI gate and delegates them to "author and independent-review verification". Deleting the review-side half left ADR-0074 delegating to nothing. |
| 8 | `## Body-edit provenance` | **restore** | The engine's own `fsgg-coord body-edits` help text names "the independent-review contract's body-edit provenance check" as the reason it exists and fails closed. That is a live cross-reference from the CLI into a deleted section. The judgement — a REST timeline silence is `NOT_MEASURED`, never a confident "unedited" — is exactly the non-answer-as-confident-negative shape, and no engine can decide it for a critic. |
| 9 | `## Root cause, dedupe, and materiality` | **restore** | The definition of **material** is the load-bearing judgement of the entire review contract. `review record` seals a verdict; it never computes materiality. The current file uses the word "material" on five lines and defines it on none of them, so every critic since the consolidation has been applying a private definition. `Verification:` `grep -n material .claude/skills/pnext-item/references/independent-review.md` → lines 1, 62, 108, 124, 163. Highest-value restoration in this set. |
| 10 | `## Game functionality` | **restore-condensed** | Blocking, authored, and un-sealable: whether a journey is driven through the product's real input surface and boots at its real entry point is read, not computed. It is also a bullet of the materiality list in row 9, so restoring 9 without 10 would leave that bullet dangling. Condensed; the `DegenerateVocabulary` carve-out is kept because it is the part that prevents a false material finding. |
| 11 | `## Disposition and repair bounds` | **split** | The machine-readable literal list (`max-automated-repair-rounds: 3`, the v1 marker names, the numbering literals) is **superseded-by-ledger** — those values are now generated into the Wire contract table, and reintroducing the marker names would red `review-round-contract.py`'s `retired_parts`. The human-park procedure (the four steps, who may retire the sentinel, that an exhausted PR never resets its counter) and the critic's four filing preconditions with the `defect`/`hardening` class rule are **restored**: they are procedure and judgement, not sealed facts. |
| 12 | `### Critic succession` | **already-restored** | The consolidation carried this forward in v2 form — the `succession` object, `grantUrl` never resolved, a grant bound to one exact head, a refusal for a generic critic identity or a self-grant. Present as prose under `## State transitions`. Restoring the long form would duplicate it. |
| 13 | `### A head that moves AFTER the chain was accepted` | **restore-condensed** | The current file states the *rule* in one sentence ("A moved head retires the accepted older generation without rewriting it"). It states none of the consequences a host must act on: the two conditions retirement requires, that retirement is a read-time exclusion and never an edit to another critic's evidence, that it is a tie-breaker only where two initial records compete, and the close-and-reopen fallback when the acceptance does not name its initial review. Those are procedure, not sealed facts. |
| 14 | `### Reading review's state` | **restore-condensed** | The engine emits the state; what a reader must **not** conclude from it is authored, and the cost of getting it wrong is measured — PR #2514 was closed and reopened as #2528 on a misreading of `malformedEvidence` over a healthy chain, costing a full fresh review. Also carries the comment-shaped-repair grant, which the engine consumes but cannot originate. |
| 15 | `### Repair phase` | **restore** | Authored procedure the ledger cannot express: automatic entry only after *validated* exhaustion, the exhausted PR closed with its counter never rewound or reused, a fresh implementer **and** a fresh critic at the route the invoking driver skill names with no downgrade or ad-hoc substitution, and no second repair phase. It is also the one section with live dangling citations: four `independent-review.md#repair-phase` links resolve to nothing today. |

Restored: 1, 3 (residue), 6, 7, 8, 9, 10, 11 (procedure half), 13, 14, 15 — eleven.
Not restored: 4 (illustration), 11 (literal half, superseded by the generated table).
Untouched because already live: 2, 5, 12.

## What protects the restoration

Nothing gated the deleted half, which is why it left silently. Two mechanisms now do, and their
division of labour is deliberate:

1. **`review-round-contract.py`** pins exact clauses and forbids the retired v1 authorities. It is
   strong but hand-maintained, and it is out of this row's declared touch-set.
2. **`check-prose-citations.py`**, extended by this change, reds when live prose links to a section
   that is not there. It is weaker per section but generalises to every document in the repository.

So the restored sections earn their protection **by being cited**: `pnext-item/SKILL.md` links into
each restored section by fragment, and the extended gate then refuses any future deletion of one.

**This claim was false when first written, for exactly one section, and the review caught it.** Round 1
measured that `### Issue and pull-request body evidence` was restored *uncited* — deleting it left both
`check-prose-citations.py` and `tests/prose-citations/run.sh` green, so the gate this work item exists to
build was blind to the deletion of the very section whose earlier loss left ADR-0074 delegating to
nothing. The root cause was upstream of the prose: `spec.md` FR-002 restated acceptance criterion 1 as
restoration plus mirror-parity and dropped the word **cited**, and the implementation followed the spec
faithfully. FR-002 now carries the citedness requirement explicitly, including that a citation from
`docs/adr/` cannot satisfy it because that prefix is exempt from the gate's subject selection.

The residual limit is stated rather than papered over: a section nothing cites is still deletable
silently, and a citation gate cannot detect a section that was *hollowed out* while keeping its
heading. Both are recorded in ADR-0074's amendment.
