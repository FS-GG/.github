# Independent review and material filing

Every item gets one independent critique cycle before merge. The implementer and critic are different
agents. The critic receives the issue, acceptance criteria, declared `Paths:`, exact PR head SHA,
complete diff, and test evidence; it does not receive the implementer's conclusions. The critic may
read code, history, issues, PRs, and the board, but **must not edit the implementation or push commits**.

The host reserves a slot for the critic and keeps the implementing worker alive until confirmation.
The critic reviews requirements coverage, correctness, regressions, tests/evidence, architecture and
ownership boundaries, release obligations, and touch-set honesty.

## Root cause, dedupe, and materiality

For every candidate finding, the critic searches the relevant code and history for the cause, then
searches open and closed issues, PRs, comments, and the board for that cause rather than only the
surface symptom. Reuse an existing item when it already carries the cause and add the new evidence
there.

A finding is **material** only when the evidence shows at least one of:

- acceptance criteria are unmet, or observable correctness, compatibility, security, data integrity,
  performance intent, or releaseability is at risk;
- a test or gate can report green without checking its declared subject;
- an architecture or ownership violation creates a concrete defect or blocks safe evolution; or
- bounded hardening prevents a measured recurring failure, retry, operational burden, or meaningful
  maintenance cost.

Style, naming taste, speculative edge cases, optional refactors, “could be cleaner” observations, and
findings already repaired in the current PR are not material new work. Record them in the review
comment when useful, but **never create an issue, board row, blocker edge, or follow-up queue entry for
them**. Uncertainty is not materiality; measure or omit.

## Disposition and repair bounds

The critic posts one durable PR comment beginning with
`<!-- fsgg:independent-review:v1 -->`. It names the reviewed head SHA, critic identity, verdict, and
each finding's evidence, root cause (or explicitly bounded unknown plus measurements), duplicate-search
result, materiality, and disposition.

The implementing worker repairs material findings that belong in the current PR. The same critic
confirms the repaired head in a reply beginning with
`<!-- fsgg:independent-review-confirmation:v1 -->` and naming the initial review comment URL and
confirmation SHA. There is exactly one initial marker and at most one confirmation marker for a given
critic and head; duplicates or competing markers fail closed until the critic supersedes them
explicitly in one final confirmation. Allow one normal repair round. A second round is allowed only
for an unresolved blocker or a blocker introduced by the first repair. Do not iterate on minor
observations.

The critic may file new work only when all of these are true:

1. the finding is material by the definition above;
2. it is a distinct root cause that cannot remain reviewably inside the current PR;
3. no existing issue already carries that cause; and
4. the evidence and acceptance boundary are sufficient for another worker to act.

The critic—not the implementer—owns filing for review-discovered findings. It files directly in the
root-cause repository, adds observed behavior, root cause or measured unknown, impact, acceptance,
verification, a narrow `Paths:`, `Class:` and `Phase`, adds the item to the correct board, and sets
`Status: Backlog` unless it is a genuine blocker. Cross-repo work follows
[cross-repo-coordination](../../cross-repo-coordination/SKILL.md). Review findings never enter the
critic's or worker's private follow-up queue.

Class the filed cause from evidence: `defect` when observed behavior violates a current contract or
acceptance boundary; `hardening` when no current contract is broken but bounded preventative work
addresses a measured recurring risk or cost. A finding that still needs human judgement is not
actionable enough for critic filing; surface it to the host.

If a filed material issue blocks the current item, the critic reports it to the host; the worker sets
the real `Blocked by` edge, parks the item `Blocked`, releases the claim, and stops. Otherwise the
critic returns `pass` only after every material finding is repaired, deduplicated, or filed. The host
verifies the marker, reviewed and confirmation SHAs, critic independence, dispositions, and every
filed issue against GitHub before merge or terminal acceptance. After verification, the host posts
`<!-- fsgg:review-accepted:v1 -->` with the accepted head SHA and initial/confirmation comment URLs.
The worker must observe that exact-SHA host marker before calling `landable` or merging.
