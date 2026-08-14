# ADR-0074: Live prose citations are local-file contracts

- **Status:** Accepted
- **Date:** 2026-08-14
- **Decision owners:** FS-GG/.github maintainers
- **Affects:** FS-GG/.github documentation policy and CI
- **Related:** [#2587](https://github.com/FS-GG/.github/issues/2587)

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

`scripts/check-prose-citations.py` gates tracked, live Markdown under `docs/`. A path is local only
when it begins with a repository-owned root/namespace. A cross-repository citation uses a URL or the
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

The gate has a deliberately small, repeatable claim and explicit false-positive boundary. It will not
catch remote-body drift or semantic prose drift; review evidence owns those classes until a structured
source exists. Expanding the parser requires a new measurement and an inversion for the added grammar.
