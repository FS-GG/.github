---
schemaVersion: 1
workId: 2563-cross-language-indent-limit
title: "the CommonMark indent limit as two constants either side of a language boundary"
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

# the CommonMark indent limit as two constants either side of a language boundary Charter

## Identity
- Work id: `2563-cross-language-indent-limit`
- Lifecycle stage: charter
- Status: chartered

## Principles
- **A rule expressed twice is one rule only while someone keeps checking.** This repository has filed
  that class at `.github#485`, `.github#865`, `.github#2347` and `.github#2544`. `#2544` collapsed it
  *inside* the engine and re-created a weaker version *across* the language boundary. The coupling
  must become mechanical, not prose.
- **Prose is not a mechanism.** `check-kit-published-coherence.py:459` declares that the file restates
  the engine's filter "rather than hold a second opinion", and `_leading_line`'s docstring declares it
  restates `DeliveryApplication.leadingLine` including the limit. Nothing reads either sentence. A
  docstring that cannot go red is documentation, not a gate.
- **The exposure today is ZERO, and that is the reason to act now rather than a reason not to.** The
  two sides agree in all 18 measured shapes. This is `hardening`: the work is to make the agreement
  survive an edit, not to repair a disagreement.
- **The mechanism must be shown FAILING.** A check that has only ever been green is not evidence
  (`.github#2551`). The demonstration is a coordinated ONE-SIDED edit: move one language's constant
  *and* whatever legs that language owns, leave the other untouched, and observe red.
- **Reachability is part of the mechanism** (`.github#2551`). A gate keyed on `paths:` is selectively
  silent. Whatever file carries the coupling must actually trigger BOTH workflows that grade it, and
  that must be demonstrated rather than assumed.
- **The direction of drift is asymmetric and one direction is worse.** A gate STRICTER than the
  engine calls a live declaration absent and stays silent about it — the invisibility that motivated
  `#2544`'s widen into the Python file. Nothing here may move the gate in that direction.
- **Criterion 5 is a hard boundary.** 0–3 spaces and leading blank lines declare; 4+ spaces or any tab
  is inert AND NAMED. Regressing it re-opens `#2544`'s fail-open, where a bystander's indented code
  sample destroyed a valid declaration already on somebody else's PR and an indented
  declaration+receipt pair read `Verified = true`. That fail-open was live for one review round.
- **Non-vacuity is not optional.** `.github#2534` measured an empty-corpus green and `.github#1768`
  measured 157 passing legs while the script was dying mid-run. A shared corpus that either side can
  silently consume zero entries of is not a coupling; it is a second way to be green while wrong.

## Scope Boundaries
- In scope: one shared, authored corpus of comment bodies and their declares/inert verdicts; its two
  consumers (the F# xunit suite and the `kit-published-coherence` shell fixture, each driving its own
  side's REAL entry point); the `paths:` declaration that makes the corpus reach both workflows; and
  the comments in both implementations that today assert the coupling in prose.
- Out of scope: any change to what declares (criterion 5); the engine's `leadingLine` algorithm; the
  Python gate's obligation-arm semantics beyond what it already does; the `#1772` tag arm; the
  `MERGE_AUTOMATION` table; and the round-1 engine-only legs (bystander destruction, the
  declaration+receipt pair, conditional advice) which are multi-comment engine behaviours the shared
  corpus's one-body-one-verdict shape cannot express.
- Explicitly out of scope: re-filing `.github#2551`.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2563-cross-language-indent-limit`.
