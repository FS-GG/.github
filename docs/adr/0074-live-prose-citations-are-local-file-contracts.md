# ADR-0074: Live prose citations are local-file contracts

- **Status:** Accepted
- **Date:** 2026-08-14
- **Decision owners:** FS-GG/.github maintainers
- **Affects:** FS-GG/.github documentation policy and CI
- **Related:** [#2587](https://github.com/FS-GG/.github/issues/2587),
  [#2660](https://github.com/FS-GG/.github/issues/2660)
- **Amended:** 2026-08-16 — section citations, see "Amendment" below

## Context

Issue #2587 measured 219 `path:line` citations in tracked Markdown. None of the 180 citations whose
files resolved had an out-of-range line, so a line-range detector would have shipped green over an
empty defect corpus. The useful defect class was narrower: live prose naming a repository-local file
that is no longer tracked. The same measurement also found that many unresolved-looking references
were legitimate citations into another FS-GG repository.

The most expensive stale claims in that investigation were issue bodies, suite-green statements,
and hand-written counts. Those strings have no stable grammar or declared authority. Treating every
number or basename as a local assertion would manufacture false positives and train maintainers to
ignore the gate.

## Decision

`scripts/check-prose-citations.py` gates every tracked live Markdown file. A path is local only when
its top-level root (or `src/` namespace) is present in the tracked repository. A cross-repository citation uses a URL or the
explicit `OWNER/REPOSITORY@REVISION:path:line` form; a bare basename is not guessed to be local.
The target must occur in `git ls-files`: merely existing in a developer's worktree is insufficient.

ADRs and dated reports are exempt. They record what was observed or decided at a point in time, so
silently rewriting their citations would falsify history. New live guidance should link to the
historical record or use an immutable repository-qualified citation when the exact old source matters.

Issue and PR bodies are outside the static repository gate. Their equivalent control is author and
independent-review verification: a body that relies on a `path:line`, count, or suite-green claim must
name its repository/revision and include the command or check URL that re-derives it. The CI tool
cannot inspect existing remote text from a source-only checkout and does not pretend otherwise.

Free-form counts and “the suite is green” shapes are also outside this detector. Generated literals
remain governed by their generators; other such claims use the same review-time evidence rule above.
They can enter CI only after gaining a structured authority and grammar.

Line-range checking is deferred. The measured corpus contained zero offenders, while line movement
alone does not establish that a historical citation became false. The gate's mandatory fixture instead
injects a genuine untracked local file and proves the predicate turns red, plus a zero-corpus leg that
must return no-verdict rather than vacuous green.

## Consequences

The workflow is unfiltered because a future tracked Markdown root is part of the subject; enumerating
today's roots in trigger filters would make the gate fail open on tomorrow's. The gate has a
deliberately small, repeatable claim and explicit false-positive boundary. It will not
catch remote-body drift or semantic prose drift; review evidence owns those classes until a structured
source exists. Expanding the parser requires a new measurement and an inversion for the added grammar.

## Amendment (2026-08-16): section citations — [#2660](https://github.com/FS-GG/.github/issues/2660)

The original decision answered **file** existence. `b84423e7` then deleted fifteen headings from
`.claude/skills/pnext-item/references/independent-review.md` while leaving the file tracked, so every
citation into it stayed green and four links kept pointing at a section that no longer existed. The
gate answered a question adjacent to the one that mattered.

**The measurement this expansion requires.** On `41199bd2`, live tracked Markdown carried 96 Markdown
link fragments — 23 naming another repository-local Markdown file and 73 naming a heading in their own
document. Four dangled, all four naming `independent-review.md#repair-phase`, and the pre-amendment
gate reported `ok` over that same tree. Unlike the line-range detector this ADR deferred for having an
empty defect corpus, the section corpus contained real offenders before a line of the checker was
written.

**The grammar, and its bound.** A citation is in scope when it is a Markdown inline link carrying a
fragment — `](#fragment)` for the same document, `](relative/target.md#fragment)` for another tracked
Markdown file in this repository. Nothing else is. A prose reference such as "the numbered steps of
X" stays out by construction: `#2660` asked for a deliberately bounded grammar and said in terms that
an open-ended natural-language claim checker was not what it wanted, and such a checker would
manufacture exactly the false positives this ADR's original reasoning rejects. Destinations carrying
a scheme, resolving outside the repository, or naming a non-Markdown file are foreign and ignored; a
fragment link whose Markdown target is untracked is a finding, symmetric with an untracked
`path:line` target.

**Quoted is inert.** A link written inside a fenced block or an inline code span is an *illustration*
of link syntax, never a live citation — real Markdown links are not authored inside code. So quoted
fragments carry no citation and raise no error, exactly as a quoted protocol marker is inert in the
review contract. This is deliberately asymmetric with the `path:line` predicate, which still reads
backticked citations because `` `scripts/x.py:1` `` is the ordinary way to *write* one, whereas
`` `](x.md#y)` `` can only be an example. The asymmetry was not theoretical: the first draft of this
amendment flagged its own plan document, which states the grammar as an inline code span. The
inertness is bounded by its own fixture leg — the identical fragment, unquoted, is still red — so
"quoted is inert" and "the predicate stopped working" do not share an exit code.

Anchors are derived as GitHub derives them — ATX headings outside fenced code blocks, lowercased with
non-word characters dropped and spaces hyphenated, `-1`/`-2` suffixes for repeats, plus explicit
`<a name=…>`/`<a id=…>`. Deriving them by parsing rather than by grepping is load-bearing and has its
own fixture leg: a heading inside a fenced block is not an anchor.

**Non-vacuity.** The section corpus carries the same zero-corpus refusal the `path:line` corpus
already had. A tree with no section citations returns no-verdict rather than green, so "no dangling
section citations" and "examined no section citations" do not share an exit code.

**Two limits, stated rather than deferred quietly.** A section that nothing cites is still deletable
silently — which is why `#2660`'s restored contract earns its protection by being cited from
`pnext-item/SKILL.md`, and why `tests/skill-quality/review-round-contract.py` remains the stronger,
hand-maintained pin for clauses too important to depend on a link. And a section *hollowed out* while
keeping its heading still resolves: this gate answers presence, never content.
