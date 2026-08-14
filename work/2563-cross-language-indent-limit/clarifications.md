---
schemaVersion: 1
workId: 2563-cross-language-indent-limit
title: Cross Language Indent Limit
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2563-cross-language-indent-limit/spec.md
publicOrToolFacingImpact: true
---

# Cross Language Indent Limit Clarifications

## Source Specification
- work/2563-cross-language-indent-limit/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking open: Which of the four candidate shapes couples the F# and Python
  implementations of the CommonMark indent limit — a cross-language coherence check, a shared
  generated constant, a fixture corpus both sides consume, or elimination of the second
  implementation?
- CQ-002 [AMB:AMB-002] blocking open: Where does the coupling artifact live, given that a workflow
  keyed on `paths:` is selectively silent about exactly the additive edits that would otherwise leave
  it unread?

## Answers
- CQ-001 [AMB:AMB-001] answer: A shared authored corpus consumed by both sides' REAL entry points.
  The reason it beats the other three is that it is the only shape whose coupling covers the WHOLE
  rule rather than one token of it. The rule has four clauses — leading blank lines are not
  indentation; 0–3 spaces still lead; 4+ spaces or ANY tab is a code block; in that case the line is
  returned AS WRITTEN so that no prefix can match — and only a behavioural corpus grades all four.
- CQ-002 [AMB:AMB-002] answer: Neutral ground at `tests/delivery-leading-line/corpus.json`, with
  `tests/delivery-leading-line/**` declared in BOTH `paths:` copies of
  `.github/workflows/coord-engine.yml`. `kit-published-coherence.yml` needs no declaration because it
  is deliberately unfiltered on `pull_request` (`.github#1597`) and therefore starts on every PR.

## Decisions
- DEC-001 [CQ-001] [AMB:AMB-001]: **Chosen — a shared authored corpus both sides consume**
  (`tests/delivery-leading-line/corpus.json`), driven through each side's real entry point:
  `DeliveryApplication.obligationsFromComments` in F#, and
  `check-kit-published-coherence.py --obligation-arm` → `obligation_declarations` in Python. The
  coupling is mechanical because the corpus becomes the ONLY place a SINGLE COMMENT BODY's
  declares/inert verdict is written for either language: the per-language single-body indent legs that
  duplicated that verdict are deleted in favour of it. A coordinated one-sided edit therefore has
  nowhere to hide — moving one language's constant reds that language against the corpus, and editing
  the corpus to restore green immediately reds the other language. The in-repo precedent is
  `tests/skill-union/skillmirror.fixtures.json`, one table graded by an F# driver, a shell conformance
  script and two Python gates.

  **What the corpus does NOT subsume, stated because an earlier draft of this decision claimed
  otherwise and the claim was false.** The F# suite RETAINS four `.github#2544` legs whose bodies are
  four-space-indented in the DECLARATION form — `DeliveryApplicationTests.fs:304` (the shared
  `indentedSample` literal), used at `:307` and `:492`, plus `:318`'s inline declaration+receipt pair.
  They are not duplicates the corpus could absorb:
    * `:307` and `:318` are MULTI-COMMENT scenarios — a bystander's sample beside a valid `none`
      declaration, and an indented declaration+receipt pair reading `Verified` — and a
      one-body-one-verdict corpus cannot express "these two comments TOGETHER yield this". `:318`
      additionally turns on a `fsgg:delivery-receipt` marker, which `obligation_declarations` never
      parses at all, so Python has no answer to compare even in principle.
    * `:492` is a single comment, but it asserts the engine's diagnostic WORDING (that the refusal does
      not advise a documentation author to make their sample declare). The gate emits no such text.
  These legs make the F# side STRICTER, never more permissive, so they cannot mask a divergence: under
  the coordinated one-sided F# edit they RED alongside the corpus. Measured, not assumed —
  `dotnet test tests/FS.GG.Coord.Cli.Tests` under that mutation reports `Failed: 4, Passed: 801`, and
  those four are exactly these legs.

  **Residual limitation, stated rather than glossed:** the corpus couples SINGLE-COMMENT BODIES ONLY.
  Multi-comment scenarios and diagnostic wording stay engine-side by necessity, and a rule change in a
  shape no entry covers is not caught. That is why the corpus enumerates the boundary from BOTH sides
  (3 spaces AND 4 spaces), carries all five tab arrangements and both CRLF cases, and why each consumer
  asserts a declared entry count and the presence of both discriminating shapes so the enumeration
  cannot silently shrink. **Rejected — a shared generated constant:**
  it introduces a build-order dependency the gate does not have today, and the dependency runs the
  wrong way — `kit-published-coherence.yml`'s fixture job installs only `setup-policy-python` and has
  no .NET at all, by design, so the generated artifact would have to be committed and would become a
  stale-artifact surface of exactly the `.github#2551` kind. It also couples only the numeral `4`,
  leaving three of the rule's four clauses uncoupled — including the tab clause, which is the one the
  `#2544` critic called easiest to get wrong. **Rejected — a cross-language coherence check over
  source text:** it asserts the literal rather than the behaviour, so it cannot see the other three
  clauses either; and it goes vacuous on any reformatting of either expression, which makes it a gate
  whose silence is indistinguishable from its agreement. **Rejected — eliminating the Python
  implementation:** the obligation arm must classify comment bodies inside a CI job that has neither a
  built engine nor network access, so deleting `_leading_line` makes the gate unable to answer at all.
  Item criterion 4 forbids exactly that, and the `#2544` history explains why the gate must keep
  answering: a gate that stops seeing declarations is the silent-invisibility direction of the drift.
- DEC-002 [CQ-002] [AMB:AMB-002]: **Neutral ground plus an explicit trigger declaration.** The corpus
  lives at `tests/delivery-leading-line/corpus.json` rather than under either language's own test
  directory, because a co-owned rule filed under one language's tests re-creates in the filesystem the
  ownership asymmetry this row exists to remove. Placing it under
  `tests/FS.GG.Coord.Cli.Tests/` would have made it reachable from `coord-engine.yml` with no
  workflow edit — that path is already in the trigger list — but it would have made the corpus look
  like the F# suite's fixture that Python borrows, which is the framing the item calls "a second
  opinion". So `tests/delivery-leading-line/**` is added to BOTH `paths:` copies of
  `coord-engine.yml`; they must remain identical or `paths-coherence` reds (`.github#880`). The
  reachability asymmetry is recorded because it is the load-bearing fact: `kit-published-coherence.yml`
  is UNFILTERED on `pull_request`, so a PR touching only `src/FS.GG.Coord.Cli/DeliveryApplication.fs`
  already starts the Python consumer; `coord-engine.yml` IS filtered, so without this declaration a PR
  touching only `scripts/check-kit-published-coherence.py` and the corpus would never start the F#
  consumer, and the gate would be selectively silent on precisely the edit it exists to catch.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. AMB-001 is resolved by DEC-001 and AMB-002 by DEC-002.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2563-cross-language-indent-limit`.
