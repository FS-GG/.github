---
schemaVersion: 1
workId: 2549-review-state-vocabulary
title: "review state vocabulary: separate a designed §6 wait from structural malformation"
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

# review state vocabulary: separate a designed §6 wait from structural malformation Charter

## Identity
- Work id: `2549-review-state-vocabulary`
- Lifecycle stage: charter
- Status: chartered
- Coordination item: `FS-GG/.github#2549`

## Principles

- **A state word is an instruction to the reader, and must be judged as one.** `malformedEvidence` is
  not merely inaccurate over a healthy chain — it is the same word the protocol uses for two competing
  initial markers, and the recovery that word teaches is *close the pull request without merging*. That
  recovery ran on PR #2514 on 2026-08-13 and cost a full fresh review. A classification that cannot
  distinguish "your evidence is broken" from "your evidence is complete and CI has not reported yet"
  is a defect even when every refusal beneath it is correct.

- **Refusals and reports are different surfaces with different failure modes.** Every refusal in this
  engine is fail-closed and stays that way: nothing unreviewed can merge, before or after this change.
  What changes is only what the *inspect* verb names, and therefore what a host does next. No guard is
  relaxed to obtain a nicer word.

- **The engine may not assert facts it cannot observe.** Whether a repair whose subject is a PR comment
  has actually been made is not derivable from `{Id; Url; Body}` comment triples: the current body is
  observable, the fact that it changed in answer to a finding is not. Following `.github#2417`
  (`CriticSuccessionReceipt`) and `.github#2175` DEC-002, an unobservable fact enters the machine only
  as an explicit, accountable receipt a caller supplies — never by inference, and never by silence.

- **Prefer evidence the engine can already observe over an out-of-band grant** (`.github#2527`'s
  charter). Applied here as a *test*, not a slogan: it is what rules a grant IN for criterion 3 and OUT
  for criteria 1 and 2. The check state is already observable, so the new post-acceptance state is
  derived, never granted; the comment-repair fact is not observable, so it is granted, never inferred.

- **Head movement is a weak proxy, and pretending otherwise rewards a no-op commit.** The rule at
  `Review.fs:401-404` treats "the tree moved" as proof an implementer did work. An empty commit
  satisfies it and proves nothing, so an implementer that wants the lane to advance is actively
  incentivised to manufacture one. Whatever replaces the rule must refuse a critic confirming a head no
  one repaired *without* making a no-op commit the cheapest way through.

- **No second marker parser, no second marker vocabulary** (`.github#2175` acceptance 11). Anything this
  change reads from a pull request is read through `Driver`'s existing classification and field grammar.

## Scope Boundaries

- In scope: the pure decision layer (`Review.fs`/`.fsi`), the chain-validation support it needs
  (`Driver.fs`/`.fsi`), the CLI snapshot boundary and JSON rendering (`ReviewApplication.fs`), the
  protocol contract text agents read (`independent-review.md` and its generated `.agents` view), unit
  coverage, and one hermetic wire fixture.
- Out of scope, deliberately:
  - `landable`'s CI verdict and its `Landable.advisoryFrom` derivation — `.github#2360` requires it to
    stay wholly independent of the review chain, and nothing here touches it.
  - `.github#2487` (`awaitingHostAcceptance` at a moved head). Same family, opposite direction; see the
    specification's own section for why it stays separate rather than being folded or silently absorbed.
  - `Protocol.reviewPolicy`'s marker vocabulary. No new marker kind is introduced, so no generated
    projection region, `.fsi` surface baseline, or `Snapshot.fs` parity fixture changes.
  - The round ceilings (3 ordinary / 10 repair-phase), the repair phase, and critic succession, all of
    which keep their existing meanings.
  - How a head is *moved*, and the live `review <ref> --pr N` path's own fact-gathering.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2549-review-state-vocabulary`.
