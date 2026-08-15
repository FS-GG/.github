# The bar, its worked rejections, and where a rejection lives

Three tests. A finding becomes a number only if it passes all three. Each one below carries a **worked
rejection**, because a bar with no recorded rejection is a bar nobody can check — a reader cannot tell
a bar that says no from a bar that has never been asked.

---

## Test 1 — Red today

> Name a command failing on `main` now, or the specific merge it blocks.

The packet's `red-today:` field answers this or it does not. "Latent", "this could bite us", and
"nothing is broken yet" are the shapes that fail it.

**Worked rejection — `author.login` cannot distinguish who filed a row (2026-08-15).**

The finding: this skill's own premise was measured as *"48 rows in 30 hours, one `author.login`, zero
filed by any worker or critic."* The whole fleet — host, implementers, critics — authenticates through
**one shared GitHub account**, which is the same property `.github#2666` was filed for. So
`author.login` is constant by construction, and the "zero by any worker or critic" half of the premise
was never measured at all. The 48 is real; the attribution is not.

*Verification:* `.github#2660`, `#2661`, `#2664` and `#2667` were all filed by named implementer and
critic identities on 2026-08-15 and every one of them reports `author.login: EHotwagner`, identical to
the rows the host filed the same day.

**Verdict: REJECTED.** No command fails on `main`. Nothing is blocked. It is a defect in a *measurement
written into an issue body* — real, worth knowing, and not a row. It goes to the register (below), and
the correction is carried in `SKILL.md` § *Honest counter-evidence* where the next reader of the claim
will actually meet it.

Note what the bar did **not** do: it did not decide the finding was wrong, or unimportant, or that the
finder was careless. Test 1 sorts by *"does the board need to track this"*, and nothing else.

---

## Test 2 — Not already derived

> If a `scripts/check-*.py` computes and reports the condition, that output **is** the tracking.

A row that restates a derived condition has a body that was true when it was written and drifts every
hour afterwards, and the only way to keep it honest is to re-measure it by hand forever. The board is
not a cache for something a gate already computes.

**Worked rejection — "the coord engine is merged but unreleased".**

`scripts/check-engine-freshness.py` exists precisely to derive this: it gates *"the engine's SOURCE
against the version the fleet can actually restore"*, and its own docstring names the four times the
condition recurred before the gate existed (`scripts/check-engine-freshness.py:2-8`).

The board tracked it anyway, and paid for it. `.github#2381` carried the standing debt with the count
in its title; the count went stale **three consecutive times**, and the row's title now ends with the
scar tissue — *"never state the count in this title — it has been stale three times"*. That row is
CLOSED, and `.github#2648` is open today carrying the same condition again with a fresh commit
in the title.

**Verdict: REJECTED as a row.** The gate is the tracking. What a *release debt* legitimately needs from
the board is the **operator decision** to publish — which is a decision, not a finding, and the scope
limit in `SKILL.md` exempts it. File the decision; never re-file the measurement.

This is also the cleanest instance of the churn pattern the reading in
[churn-reading](churn-reading.md) looks for: one derived condition, two rows, two years of
titles that cannot stay true.

---

## Test 3 — Class-anchored

> If an open row already proposes the mechanism that prevents this finding's whole class, the finding
> is **evidence on that row** until the class row lands.

This is the test that stops one cause becoming seven numbers. It is also the one most often skipped,
because the instance in front of you always looks more concrete than the class row.

**Worked rejection — a worktree's own engine build shadows the shared one (2026-08-15).**

The finding, first-hand: an implementer on `.github#2584` had to run `dotnet build src/FS.GG.Coord.Cli
-c Release` inside its own worktree, because `tests/skill-quality/run.sh` seeds its fixture tree from
`src/FS.GG.Coord.Cli/bin/Release/net10.0` and cannot run without one. That build makes
`scripts/fsgg-coord` prefer the worktree's engine over the shared checkout's for the rest of the item —
so a later staleness refusal names the worktree, not the shared checkout, and the printed remedy points
at the wrong tree.

**Verdict: REJECTED as a new row.** `.github#2653` is open and states exactly this class — *"a gate
harness's incidental engine build shadows tier 2b, so stale_guard refuses every board write from that
worktree"* — and names three occurrences in one run. The `skill-quality` harness is a **fourth
occurrence**, not a fourth cause. Transplant it onto `.github#2653` as evidence, with the exact harness
and the line that forces the build, and file nothing.

The tell that this is test 3 and not a duplicate check: the finding and the class row surface in
different places and share no symptom text. Deduping on the **symptom** would have missed it.
Deduping on the **cause** — "an incidental local engine build shadows tier 2b" — finds it immediately.
This is why the packet's `cause:` field is required and its `surface:` field is not sufficient.

---

## Where a rejected finding lives

**"Rejected" must never mean "forgotten".** A bar that discards what it refuses is a shredder, and the
next worker in that area re-finds the same thing and pays for the same analysis.

The obvious home does not work. `scripts/fsgg-coord followup add` is *this worker's* queue — keyed on
the resolved worker id, stored as a local file, and designed as the *"I can fix this, just not in THIS
PR"* promise a worker makes to itself. You hold no claim, open no PR, and make no such promise; your
rejections have to survive for whoever eventually claims the area, who is someone else.

So a rejection lands in **two** places, and both are cheap:

1. **On the row where it will be looked for**, when there is one. A test-3 rejection goes onto the
   class row as evidence — that is the disposition, not a consolation prize, and the eventual claimant
   of that row is the exact reader who needs it. A test-2 rejection goes onto the derived gate's own
   row if one is open.

2. **On the rejected-findings register**, always. One issue per repository, titled exactly
   `board-analyst: rejected findings register`, holding one comment per rejection: the packet verbatim,
   the test that rejected it, and the reason. One search finds every rejection this bar has ever made.

**The register is deliberately OFF the board.** It is never passed to `scripts/fsgg-coord add`, carries
no `Status`, no `Class` and no `Paths:`, and is therefore invisible to every scheduler — it can never be
claimed, ranked, or counted as a row. That is a shape this org already uses rather than an invention:
coordination rooms are off-board issues that close themselves, and ADR-0041's chore lock is an off-board
issue too. A register that were a board row would make the bar's own bookkeeping into the churn it
exists to prevent.

Create it once, on first use, and never a second one; find it by its exact title before creating
anything. Record each rejection like this:

```sh
gh api -X POST repos/FS-GG/.github/issues/<register>/comments -f body='…'
```

Sign every rejection with your minted id, and give it a `Verification:` line. A rejection with no
evidence is indistinguishable from an opinion, and the next reader has no way to reopen it.

---

## The seam this creates, stated honestly

`SKILL.md` says you are the only actor authorised to **create** a row. There is one place that clause
meets an existing contract, and it must not be resolved by writing softer prose in either file.

`independent-review.md` gives the **critic** ownership of the disposition of its own review-round
findings, and `findings-and-filing.md` states that once review has started the critic *"owns the
disposition of the findings it raises"*. That ownership is not a filing convenience — it is what keeps
the review independent, and a critic whose material finding needs a third party's permission to become
a number is a critic with less authority than the contract grants it.

The reconciliation this skill implements, and its limit:

- **Materiality stays with the critic, absolutely.** You never overturn a critic's materiality
  judgement, and you never decline a finding on the ground that a critic should not have raised it.
- **A critic files what its contract lets it file.** Your monopoly covers findings routed through
  `findings-and-filing.md` — the implementer's pre-review findings, the host's, and your own. It does
  not reach into a live review round, because nothing may block one.
- **You adjudicate afterwards.** Rate and granularity are properties of the sequence, and the sequence
  is visible only after the fact. Fold, retitle, or close-with-reason a review-filed row in a later
  pass, exactly as you would any other row.

**If a reader concludes that this makes the monopoly partial, they are right, and it is deliberate.**
The alternative — a critic that must queue its finding behind an analyst pass — buys granularity with
a wedged review chain, and a wedged chain is the more expensive failure. Whether the monopoly should be
extended over review-round filing is a **route and scope question for the host and the operator**, not
something to settle by editing this paragraph.
