---
schemaVersion: 1
workId: 2583-consolidation-tax
title: "consolidation tax: distinguish a route-neutral consolidating body edit from a scope change"
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# consolidation tax: distinguish a route-neutral consolidating body edit from a scope change Charter

## Identity
- Work id: `2583-consolidation-tax`
- Lifecycle stage: charter
- Status: chartered
- Coordination item: `FS-GG/.github#2583`

## Principles

- **The fix is not "hash less."** `.github#2392` narrowed the subject deliberately, and its own source
  comment (`Client.fs:440-447`) declares semantic edit-classification out of scope rather than
  unnecessary. Dropping more lines from the subject would buy consolidation by making a genuine scope
  change invisible — destroying the exact property the receipt exists to provide. Whatever this work
  does, the set of lines that are hashed does not shrink.

- **Route-neutrality is a property of the edit, not a shape of the diff.** The acceptance criteria ask
  for a rule, not a special case for appending. A delivery-route decision judges a set of statements
  about the item. It is invalidated when a statement it judged stops holding — not when statements are
  added beside the ones it judged. That yields one rule, stated once:

  > An edit is **route-neutral** when every subject line the receipt judged is still present, in the
  > same relative order, byte-identical — i.e. the judged subject survives as an ordered **subsequence**
  > of the current subject. It is a **scope change** when any judged line was modified, removed, or
  > reordered.

  Insertion anywhere qualifies, including mid-body, which is what folding a cause into an existing
  `## Root cause` or `## Dedupe` section actually looks like. Append is not privileged; it is merely the
  easiest instance of the rule.

- **Additive acceptance must be visible, never silent.** The rule above cannot read intent, and an
  insertion *can* redefine scope (`## Also: migrate every downstream repo` is an insertion). This is the
  work's most dangerous edge and it is accepted knowingly, on the grounds that the row's whole purpose
  is to keep such a row schedulable. It is therefore paid for with reporting: a read that resolves
  additively says so, and says how many lines were added, rather than being indistinguishable from
  "nothing changed."

- **Strictly widening, never narrowing.** No body/receipt pair that is `Current` today may become
  `Stale`. The new acceptance is a third candidate consulted only after `.github#2392`'s canonical and
  legacy candidates have both declined, so both of those are reached on byte-identical inputs and
  `.github#2392` AC5's migration bridge is preserved by construction rather than by care.

- **A derived binding is not a judgement, and must not be charged to the author.** The judged-line
  record is a mechanical function of the very body whose `subjectRevision` the authoring agent already
  had to get right. It is therefore derived by `delivery-route record` from the body it just validated
  that revision against — inheriting that proof exactly — rather than transcribed by hand into the
  receipt JSON.

- **The scheme lives where the other two schemes live.** `deliveryRouteSubject` is built on
  `Markdown.classify`, and `.github#2392`'s comment records that `DeliveryRoute.fs` compiles *ahead of*
  `Markdown.fs` in `FS.GG.Coord.Core.fsproj`. The subject scheme therefore physically cannot live in
  Core's `DeliveryRoute.fs`; it lives in `Client.fs`, next to the canonical and legacy candidates, and
  the third candidate joins them there. This is a compile-graph fact, not a diff-size preference.

- **Comments were never in scope of the hash and still are not.** Only the issue body is hashed. The
  answer is stated explicitly in the source so the next reader does not re-derive it from
  `deliveryRouteSubject`'s line filter.

## Scope Boundaries

- In scope: the subject-revision candidate set and the receipt comment envelope in
  `src/FS.GG.Coord.Cli/Client.fs`, and command-boundary coverage in `tests/FS.GG.Coord.Cli.Tests`.
- Out of scope: `DeliveryRoute.decide`/`validate` policy in Core (unchanged), `Schedulability`'s mapping
  of `Stale` to `AwaitingDeliveryRouteDecision` (unchanged — an additively-current row is `Current`, so
  it never reaches that mapping), the `Paths:`/`Class:`/`Blocked on:`/`Blocked by:` exclusion
  (unchanged), and `legacyDeliveryRouteRevision` (unchanged).
- Out of scope: any semantic judgement of *what was inserted*. The rule is structural; the visibility
  requirement above is how that limit is discharged rather than hidden.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2583-consolidation-tax`.
