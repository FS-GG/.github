# The churn reading

The bar decides one finding at a time. This decides whether the board is healthy, and it is the output
that makes this role answerable for something a filing gate can never be answerable for.

**A count is not a reading.** "Net +12 today" is a number; a reading says what shape produced it and,
if that shape is pathological, what mechanism would stop it. A board that grows because a wave of real
work arrived and a board that grows because one cause is being re-filed under seven titles produce the
same number and need opposite responses.

## The one metered read

```sh
scripts/fsgg-coord issues .github --state all --refresh
```

That is the whole input for parts 1 and 2. It is REST and ETag-revalidated — a 304 costs nothing — and
it returns every issue with `created_at`, `closed_at`, `state` and `title`, which is enough to derive
the window arithmetic locally with `jq` or `python3`. **Do not reach for a board `scan` here.** A scan
costs on the order of 1,900 REST requests of an hourly 5,000 shared with every live worker, and it
answers a scheduling question, not a churn question. The single `scan` this role is allowed is the lane
check after filing.

Pull requests share the issue number space; exclude anything carrying `pull_request` before you count,
or every merged PR inflates both halves and the net delta stays accidentally right for the wrong
reason.

## The five parts, all five required

### 1. Net row delta over a stated window, beside how many items landed in it

State the window. A delta with no window is not falsifiable, and "today" drifts.

The second half is what makes the first half mean anything: **+3 against 30 landed is a healthy board
absorbing discovery; +3 against 1 landed is a board being filled by something that is not doing work.**
Report both numbers or neither.

### 2. The rows that are instances of one cause, named as such

Not "possible duplicates" — instances. Two rows are instances of one cause when a single mechanism
landing would retire both. Name the cause in your own words, then name the rows, then say which one
should carry it. Fold the others onto it and close them with the reason recorded first.

Dedupe on the **cause**. Rows describing one cause routinely share no symptom text at all, and a
title-similarity pass finds none of them.

### 3. Any cause row whose repairs keep generating successors

This is the most expensive pattern on the board and the hardest to see from inside any one item,
because each successor is individually well-evidenced and correct.

The tell: row A lands, and the PR that lands it produces row B about the same subject; B lands, and
produces C. Nobody did anything wrong at any step. The cause is that the *class* was never named — each
repair fixed the instance in front of it. The remedy is always the same shape: **stop repairing
instances and file the class row**, with the mechanism that makes the whole family unreachable, then
anchor the successors to it under test 3 of the bar.

### 4. Any row restating a condition a `scripts/check-*.py` already derives

Grep the gate corpus for the condition before you accept that a row is tracking something. A row that
restates a derived condition drifts the moment it is written and can only be kept honest by re-measuring
it by hand — which somebody then does, forever. Test 2 of the bar exists to stop these being created;
this part of the reading catches the ones already on the board.

### 5. When a pathology is present, a proposed remedy naming the mechanism

A remedy names a mechanism, not an intention. *"Be more careful about duplicates"* is not a remedy.
*"Fold #A and #B onto #C and close them; the successors stop because #C's acceptance criteria name the
class"* is a remedy — someone can do it and someone else can check whether it worked.

### A sixth shape, which the five parts above will miss unless you look for it

**Rows that are individually green and jointly red.** Two items are correct in isolation, pass every
gate on their own branch, and turn `main` red the moment the second one merges — because they interact
through a **gate corpus neither author controls**. Nothing in the per-row arithmetic can see this:
both rows are real, neither is a duplicate, neither restates a derived condition, and neither generated
the other.

The tell is a gate whose subject is *"every file of kind X"* while two live items are each adding a
new file of kind X. Look for it whenever a pass sees two open rows touching the same gate's corpus
from different directions.

**Worked instance, 2026-08-15.** `.github#2666` added `tests/skill-quality/recency-comment-edit.py`,
which requires **every** `.claude/agents/*.md` to carry one canonical hazard statement byte-identically.
`.github#2584` independently added two new `.claude/agents/*.md` route bindings. Each was green on its
own branch — `#2666`'s gate saw four definitions and passed; `#2584`'s tree had no such gate. Merged in
either order, `main` reds, and it reds in a file whose author never saw the gate.

*Verification:* the merged tree was simulated (`#2666` at `385e132e` overlaid with `#2584`'s working
tree) and `recency-comment-edit.py` run over it: green at **6** definitions once both new bindings
carried the statement verbatim, and exit 1 — *"carries the canonical recency-edit hazard statement 0
time(s), not exactly once"* — the moment one dropped it.

**Remedy, and it is a mechanism rather than a warning:** when a pass sees this shape, say which of the
two rows should carry the coupling and how it is discharged *before* either merges — normally by the
later row adopting the corpus requirement from the earlier row's branch, verified against a simulated
merge rather than against its own branch. A note asking both authors to "watch out for each other" is
not a remedy; a named owner and a run against the merged tree is.

### And when there is no pathology, say so with the measurements

**A silent pass is not a clean pass.** If the reading is healthy, state that explicitly and show the
numbers that support it. Otherwise the difference between "the analyst looked and the board is fine"
and "the analyst did not look" is invisible, which is the same defect as a gate that has never been red.

## A worked reading — FS-GG/.github, 2026-08-14T17:00:00Z to 2026-08-15T17:00:00Z

**Both endpoints of that window are in the past, and that is not a stylistic preference.** It is the
rule in part 1 applied to this document: a worked example whose window has not closed cannot be
re-derived by the next reader, and an exemplar that cannot be re-derived teaches the opposite of what
this file asks for. Read § *Two windows, one board* below before copying these numbers anywhere.

**1. Net delta beside items landed.** 17 rows opened, 24 closed: **net −7**, with 28 open at the time
of measurement.

*Verification:* `scripts/fsgg-coord issues .github --state all --refresh`, filtered on `created_at`
and `closed_at` within the closed interval above, excluding entries carrying `pull_request`. The read
returns the whole corpus (1147 issues); do not cap it — see § *Two windows, one board*.

**2. Instances of one cause.** `.github#2381` (CLOSED) and `.github#2648` (OPEN) are two rows for one
cause: *the coord engine is merged and unreleased*. `#2381` carried the count in its title and went
stale three consecutive times — its title now ends *"never state the count in this title — it has been
stale three times"* — and `#2648` re-files the same condition with a fresh commit in the title.

**3. Repairs generating successors.** The `guarded-boundary.py` chain, three generations deep and every
link inside this one window:

| generation | filed | by | out of |
| --- | --- | --- | --- |
| 1 | `.github#2652` | independent review | PR #2644, the repair of `.github#2571` |
| 2 | — landed as PR #2665 | | |
| 3 | `.github#2667` | independent review, critic `wren-aa9f` | PR #2665, the repair of `.github#2652` |

*Verification:* `.github#2667`'s own body — *"Found by independent review of PR #2665 at head
`c3a7f868…` (critic `wren-aa9f`, initial round), and deliberately NOT blocking that head"*, and
*"filed rather than blocking #2665 for the same reason `.github#2652` itself did not block #2644"*.

**Read this carefully before calling it carelessness, because it is the opposite.** Every link is
correctly argued: `#2667` states plainly that it is *"a pre-existing blind spot, not a regression"*,
dedupes against what `#2652` actually closed, and deliberately declines to block the head it was found
on. Each filer, looking at its own link, made the right call. The pattern is only visible from **three
links up**, which is a vantage no participant in any single review has — and that is precisely the
thing this reading exists to supply.

**4. Rows restating a derived condition.** `.github#2648`, again. `scripts/check-engine-freshness.py`
derives *"the engine's SOURCE against the version the fleet can actually restore"* and its docstring
names four prior recurrences (`scripts/check-engine-freshness.py:2-8`). The board is caching a gate's
output.

**5. Remedy, and the pathology verdict.**

This window is **not** pathological on rate: it is net negative, and it closed more than it opened
against a working fleet. Set it beside the reading that motivated this whole role — *net +12 in 24
hours against 25 landed*, over the 30 hours to `2026-08-14T05:00:00Z`. Two closed intervals a day
apart, on the same board, running in opposite directions. **A reading taken once and quoted thereafter
would be describing a board that no longer exists**, which is why this output is required every pass
rather than produced once as an argument for a change.

**A closed past window is reproducible forever, and the earlier figure demonstrates both halves of why
that matters.** Sweeping every hourly 24h window in that era against the live corpus, *net +12* comes
back exactly — 43 opened, 31 closed — for the 24 hours ending `2026-08-14T05:00:00Z`, the endpoint the
row's own 30-hour window names. The delta survived a day of further board activity untouched, because
`created_at` and `closed_at` are timestamps and a later event simply falls outside the interval.

Its companion clause did not survive. *"While 25 items landed"* reproduces **nowhere**: no 24h window
anywhere in that sweep closed 25 issues, and the value at the matching endpoint is 31. The number is
not wrong — it is almost certainly counting merged PRs or `Done` transitions rather than issue
closures — but the row never says which, so it cannot be checked, and a reader re-deriving the reading
gets a different answer and no way to tell whether the board changed or the definition did.

That is the whole discipline in one example: **the half that named its window and its unit came back
identical a day later; the half that named neither is unfalsifiable.** State the window, and state
what "landed" counts.

Two structural pathologies are present and neither is a rate problem:

- *Derived-condition caching.* Remedy: close `.github#2648` as a row and carry the release debt as an
  **operator decision** instead — the scope limit in `SKILL.md` exempts decisions from test 1, and
  `scripts/check-engine-freshness.py` already reports the condition continuously. This retires the row
  and loses no tracking.
- *A three-generation repair chain in `guarded-boundary.py`.* Remedy: name the class on `.github#2667`
  itself — *every touch of a loaded decision program sits behind one fail-closed boundary* — and make
  that the acceptance criterion, so generation four is **evidence on `#2667`** under test 3 rather than
  a fourth number. Do not ask the reviewers to be more careful; each of them was already right.

- *One coupling of the sixth shape.* `.github#2666` and `.github#2584` are individually green and
  jointly red through `recency-comment-edit.py`'s corpus. Remedy: `.github#2584`, as the later row,
  adopts the canonical statement from `#2666`'s branch verbatim and verifies it against a **simulated
  merged tree**, not against its own branch. Discharged before either merged.

**Filed as a result of this reading: nothing.** Every remedy above is a disposition of an existing row
or a sequencing decision, which is the outcome a healthy pass should usually produce.

## Two windows, one board — why part 1 says "state the window"

This is not a cautionary tale about somebody else. Two readers of *this* board, reading *this* corpus
on 2026-08-15 with correct arithmetic, reported **net −7** and **net −1** and both were right.

| window | opened | closed | net |
| --- | --- | --- | --- |
| rolling 24h, `2026-08-14T17:00:00Z` → `2026-08-15T17:00:00Z` | 17 | 24 | **−7** |
| UTC calendar day so far, `2026-08-15T00:00:00Z` → `2026-08-15T17:00:00Z` | 17 | 18 | **−1** |

Both intervals are closed. Neither reader made an arithmetic error, and neither number is a better
measurement of the board than the other. **They are answers to different questions**, and both were
labelled "today".

The entire difference is six closures — `.github#2409`, `#2561`, `#2580`, `#2582`, `#2586`, `#2587` —
all of which landed on the evening of 2026-08-14, between `18:26Z` and `21:21Z`. The rolling window
reaches back over that evening; the calendar day starts after it.

**And here is the part that makes this worth a whole section.** The *opened* sets of the two windows
are **identical** — the same 17 rows, not merely the same count — because **zero issues were created
in that 08-14 evening tail**. So the two readings differ in exactly one column. That coincidence is
what made the disagreement look like a closed-count bug in one reader's arithmetic, and it cost real
time to resolve as what it actually was: a window mismatch, with no bug on either side.

Three rules follow, and they are why part 1 is worded the way it is:

1. **State the window as a closed interval with explicit UTC endpoints.** Not "today", not "the last 24
   hours", not a window whose end has not happened yet. A reading whose window has not closed cannot be
   re-derived even by the person who wrote it, five minutes later.
2. **Two readings that disagree are a window question first, an arithmetic question second.** Diff the
   *sets*, not the counts — the six numbers above are what a set diff produces immediately and what
   comparing totals hides.
3. **Never conclude from agreeing counts that two readings used the same window.** Here the opened
   counts matched exactly, and the windows were a full seventeen hours apart.

**Do not cap the corpus read.** `--limit`-style truncation on the underlying issue list is the same
failure in a different dimension: it silently drops the rows outside the cap and produces a confident,
unfalsifiable count. `scripts/fsgg-coord issues .github --state all --refresh` returns the whole
corpus; filter it locally.
