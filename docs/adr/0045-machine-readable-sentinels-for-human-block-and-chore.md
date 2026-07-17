# ADR-0045: A body-line sentinel says what an empty field cannot — human-blocked, and file-less chore

- **Status:** Accepted
- **Date:** 2026-07-17
- **Affects:** FS-GG/.github (the protocol, the engine, `lint`); every repo whose items are scheduled by `fsgg-coord`

## Context

Three times over, one board field collapses two OPPOSITE facts, and the engine cannot tell them apart.
[.github#1103](https://github.com/FS-GG/.github/issues/1103) (split from [#669](https://github.com/FS-GG/.github/issues/669)
legs 2 and 8) named the pattern:

- **A human-blocked item.** `Blocked by` is **ref-typed** — it holds `owner/repo#n`, and structurally
  cannot say *"blocked on a person"*. So an item deliberately parked on a human renders with an **empty**
  `Blocked by`, identical to one where somebody simply forgot to name the blocker. Measured, the class is
  3/3 deliberate parks, 0/3 filing errors ([#574](https://github.com/FS-GG/.github/issues/574) on an
  org-admin action; [#498](https://github.com/FS-GG/.github/issues/498) on a decision; #669 itself). And
  the two parks are not the same: #574 is startable the moment a scope is granted; #498 could not start
  until a human chose. One empty field flattens *action*, *decision*, and *mistake* into one rendering.

- **`Paths: none`.** `Schedulability.DeclaredNone → DeliberatelyNoTouchSet` means BOTH *"an epic has no
  touch-set (unschedulable by design)"* AND *"a chore's touch-set is empty (trivially schedulable)"*. The
  scheduler treats them identically, so a file-less chore is refused exactly as an epic is.

The human-block collapse is the one that bit hardest, one axis over. [.github#918](https://github.com/FS-GG/.github/issues/918)
is a **decision** item ("Why this is a DECISION and not a fix", in its own body) carrying a **real, wide
touch-set** — the anticipated fix-scope, correctly recorded. It is unschedulable-by-design because a human
must decide it first, but nothing on the board says so: the column is `Backlog`, and
`take --include-backlog` is a documented idiom, so #918 was handed to the next `/pnext-item` worker as
ready work. On 2026-07-16 [#732](https://github.com/FS-GG/.github/issues/732) was claimed by **four**
workers in one hour, each reaching the same conclusion the others had already recorded — the same waste
[#1081](https://github.com/FS-GG/.github/issues/1081) measured on #918. The `Backlog` column is a
*"not yet"*, not a *"not for you"*, and it cannot carry the difference. `Paths: none` can express "not
for you", but only by discarding the fix-scope the filer recorded — the discriminating axis
(**needs-a-human-decision** vs **schedulable-work**) is *orthogonal* to whether a touch-set exists.

## Decision

**Recorded by @EHotwagner (org owner) on #1103, 2026-07-17.** A machine-readable sentinel, carried as a
**body line** parsed like `Paths: none` (the [#496](https://github.com/FS-GG/.github/issues/496)
mechanism) — not a new Projects v2 board field. It is the cheapest option, lives with the item, adds no
board-schema churn, and every filer already writes body lines, so `lint` can enforce it directly.

Concretely, both share the `Paths:` grammar — a line at up to three leading spaces, OUTSIDE any fenced
code block (a fenced line is a quotation, not a use — #277):

1. **Human-block.** `Blocked on: human/decision` (unstartable until a human CHOOSES) or
   `Blocked on: human/action` (blocked on a human ACTION such as a scope grant; startable the moment it
   lands, not before). The scheduler refuses such an item **regardless of its `Paths:` line**, so a
   decision item keeps the real touch-set recording where its fix will land. The action-vs-decision
   distinction is load-bearing and is preserved on the wire (the verdict's `humanBlock` detail).

2. **Chore.** `Paths: any` — a file-less chore that reserves nothing and conflicts with nothing, so it is
   **schedulable** and runs alongside any concurrent item. It is the counterpart to `Paths: none`
   (unschedulable): both reserve nothing, only `any` is schedulable. This is a **deliberate,
   parser-verified empty reservation** — not [#273](https://github.com/FS-GG/.github/issues/273)'s
   fail-open, a path-shaped token that reserves nothing by *mistake*. The claim lock still serialises the
   claim, so exactly one worker holds a chore at a time.

3. **`lint`** reds an empty `Blocked by` on a `Blocked` item **only when no sentinel line is present** —
   the author had a machine-readable way to say what they meant and used neither a blocker ref nor the
   sentinel (`BLOCKED-NO-REASON`).

The engine owns both sentinels in the typed core: `HumanBlock` (`Types.fs`, parsed by `HumanBlock.parse`),
a `DeclaredChore` `TouchSet` case distinct from `DeclaredNone`, and an `AwaitingHuman` `Schedulability`
verdict checked after the concrete blockers and before the touch-set.

## Consequences

- A decision item is now **refused by construction**: it declares `Blocked on: human/decision` and keeps
  its real `Paths:`. `take`/`batch` withhold it in every mode, `--include-backlog` included — closing the
  door #918/#1081 walked through. An agent can no longer be handed the architectural call an item exists
  to escalate.
- Every future human-park and every epic-vs-chore is filed with a sentinel; `lint` reds an unmarked
  `Blocked` park (`BLOCKED-NO-REASON`), so the omission is caught at filing time rather than read as a
  deliberate park.
- **This PR marks the known instances:** #918 (`human/decision`), #574 (`human/action`), #498
  (`human/decision`). Items filed before this ADR that are parked on a human still render an empty
  `Blocked by`; `lint` will now name them, which is the intended migration signal, not a regression.
- `Paths: none` is **unchanged** — every existing item keeps its meaning. Only a filer who writes
  `Paths: any` opts into the new schedulable-chore behaviour.
- A concrete `Blocked by #n` still outranks the sentinel in the scheduler's verdict, because it is the
  more actionable sentence; the sentinel governs only once the concrete blockers are clear.

## Alternatives considered

- **A dedicated Projects v2 board field** for the human-block reason. Rejected on #1103: board-schema
  churn, a field every filer would have to learn, and a value `lint` cannot enforce as cheaply as a body
  line it already reads.
- **Stripping #918's touch-set to `Paths: none`** to make it un-takeable today. This is the only lever
  available *without* this ADR, and it discards the fix-scope the filer correctly recorded — the reason
  #1103 was filed rather than papered over. The sentinel keeps both facts.
- **The `Paths: any` spelling and its "reserves nothing, schedulable" semantics** are an implementation
  choice within the "same grammar" #1103 delegated for the epic-vs-chore split — the decision fixed the
  *mechanism* (a body-line sentinel) but not this token. `any` names what the chore is compatible with
  (any concurrent touch-set), consistent with how the engine reasons about disjointness. Called out here
  so a reviewer can overrule the token without reopening the decision; the alternative considered was
  `Paths: chore`, rejected because it collides with the engine's internal derived-`Chore` vocabulary.
- **Two wire `kind`s** (`awaiting-human-decision`/`-action`) for the verdict. Rejected: the union has one
  `AwaitingHuman` case and `Protocol.verdicts` is pinned 1:1 to the cases by reflection, so the
  distinction rides as verdict *detail*, exactly as `wrong-status` carries its column.
