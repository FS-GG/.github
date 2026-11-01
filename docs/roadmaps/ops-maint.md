# OPS-MAINT — standing plan for unplanned fixes and hygiene

This is the owning plan for the standing Outcome [FS-GG/.github#5097](https://github.com/FS-GG/.github/issues/5097).
Root/fdev owns intake, classification and acceptance. Each Delivery has its own owner, named on its issue. Live
state (what is merged, open, blocked or enrolled) is kept on the issues and the board, not in this plan. Read
those, not this file, for current status.

## Standing promise

The Outcome is a standing container. It is never closed and has no end date. Its promise is that an unplanned fix
or piece of hygiene that belongs to no other Outcome is not lost and not done off the record: it gets its own
Delivery, is accepted on its own evidence, and leaves the organization's checks, census and automation correct
afterwards.

Examples of what belongs here: an emergency fix to main, a broken scheduled check, a stale census hash, a flaky
test. A unit of work that belongs to a planned promise is filed under that promise's Outcome instead, and a unit
that grows past one bounded Delivery is split or promoted to its own Outcome rather than kept here.

## Delivery shape

Under the dispatch rule ([FS-GG/.github#5092](https://github.com/FS-GG/.github/issues/5092)) every such fix gets
its own Delivery issue, filed as a native sub-issue of the Outcome. The body is one line:

- the owner,
- the done-when, stated as a verifiable result (a check that is green, a hash that matches, a test that passes
  repeatedly),
- the `Refs` to the Outcome and to any related issue or PR.

A Delivery is not a catalogue of future work. It names one fix and one acceptance. A recurring cause that needs a
decision or a design is a separate Outcome, not another Delivery here.

## Acceptance rule

Root/fdev programme decision 9, recorded on
[FS-GG/.github#4730](https://github.com/FS-GG/.github/issues/4730#issuecomment-6551301543):

- A Delivery is closed when all of its linked PRs are merged and none is open. Root applies the rule by sweep until
  the board tend implements it.
- Exception: the last merge carries `fsgg:delivery-continues/v1`. The Delivery then stays open for the work the
  marker says remains.
- The rule is limited to Delivery issues. It does not change that the Outcome is never closed, and it does not
  extend to other issue types.

A source merge does not discharge an installed, published or recovery step. A Delivery that owes one states it in
its done-when, and its last source merge carries the continues marker until that step is read back.

## Sequencing and ownership

1. Intake first: Root/fdev classifies the item (belongs here, belongs to another Outcome, or needs its own
   Outcome) and files the Delivery sub-issue before work starts.
2. One owner and one PR chain per Delivery, under the repository's normal review and required checks. Emergency
   fixes use the same route, only sooner; they are not exempt from review or checks.
3. A fix that reveals a systemic cause files the cause separately, and the Delivery stays scoped to the fix.
4. Deliveries are independent of one another unless a Delivery's own issue names a dependency.

## Out of scope unless a Delivery accepts it

- Planned feature or migration work, which belongs to its own Outcome and plan.
- Closing or rewriting any Delivery other than by the acceptance rule above.
- Widening a protected policy, required check or permission without a recorded decision.
