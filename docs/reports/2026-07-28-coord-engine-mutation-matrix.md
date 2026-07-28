# `#1794`'s mutation matrix, re-measured under repaired anchors — 2026-07-28

**Subject:** the ten guards `.github#1794` added to `Reads.openIssues` and its two call sites, filed as
`#1825`.
**Why it exists:** `#1794` measured **four of ten** legs inside its window and reported the other six as
`NOT MEASURED` — never as passing. It also found an anchor defect **in its own harness** and
deliberately left it unfixed, because changing anchors after measuring ships a harness differing from
the one that produced the evidence.
**Method:** break one guard, rebuild the engine, run every check that claims to defend it, and record
whether the checks fire.
**Harness:** `scripts/lib/mutation.py` + `scripts/gate-mutate.py` — the harness `#1810` built for
`#1808`'s decision, not a re-typed bespoke script.
**Reproduce:** `scripts/gate-mutate.py --specs tests/coord-engine-mutation/specs.yml`

**Harness under measurement:** `scripts/lib/mutation.py` at `b3671ea`
(sha256 `d2b7c584…`), `scripts/gate-mutate.py` at `656c401`. Twenty runs — ten controls, ten mutants —
each an engine rebuild plus 838 unit assertions plus the 641-assertion parity corpus. ~48 minutes.
`gate-mutate.py` exited **0** (every leg measured, every gate fired).

## Verdict

**All ten legs KILLED. 0 ESCAPED. 0 NOT MEASURED. Control green before every one of them.**

| leg | guard reverted | verdict | killed by |
|---|---|---|---|
| **M1** | an unidentifiable element is dropped again | **KILLED by 4** | unit `NO number REFUSES the read`, unit `number is the STRING 42 refuses`; parity `an unidentifiable element REFUSES the read`, `the refusal must locate the element within the merged array` |
| **M2** | an absent `body` reads as `""` again | **KILLED by 8** | unit `an ABSENT body field is BodyUnread`, unit `an OFF-BOARD live claim … reserves an UNKNOWN surface`; 6 parity incl. both `#1794×#1792` lapsed legs |
| **M3** | an ill-typed `body` reads as `""` again | **KILLED by 5** | unit `an ILL-TYPED body is BodyUnread`; 4 parity (`illtyped`) |
| **M4** | `body: null` becomes unreadable (over-correction) | **KILLED by 1** | unit `a NULL body is BodyRead empty` — **and nothing else** |
| **M5** | `activeCollisions` discards an unreadable row via the token filter | **KILLED by 10** | 10 parity — **0 unit** |
| **M6** | `activeCollisions` skips a held unreadable row instead of refusing | **KILLED by 10** | 10 parity — **0 unit** |
| **M7** | `activeCollisions` refuses on *any* unreadable row (over-correction) | **KILLED by 2** | parity `an unheld row's unreadable body must not red the scan`, `unheld unreadable row should leave the verdict at ExitContended=6` |
| **M8** | `Scan.snapshot`'s off-board sweep reserves nothing | **KILLED by 1** | unit `an OFF-BOARD live claim whose body could not be read reserves an UNKNOWN surface` — **and nothing else** |
| **M9** | engine pagination disabled (`Transport.Send` stops following `Link`) | **KILLED by 34** | 3 unit + 31 parity |
| **M10** | the fixture cannot bind, so `refute` must report NOT MEASURED rather than PASS | **KILLED by 21** | 14 parity FAIL + **7 parity NOT MEASURED** |

### Which numbers are re-measured and which are quoted

**All ten are re-measured. Nothing is quoted.** `#1794`'s four verdicts were deliberately *not* carried
forward: the anchors changed underneath them, and a verdict produced by a different harness is a
different verdict.

Comparing anyway, because the deltas are the evidence for the anchor fix:

| leg | `#1794` said | this run | why |
|---|---|---|---|
| M1 | KILLED by 5 | **KILLED by 4** | `#1794`'s fifth was `…never silently dropped so the scan reaches a COLLISION verdict without it`, which under its guard-produced anchor reported **NOT MEASURED** and was *counted as a kill*. Under the repaired anchor the leg reaches a verdict and **correctly passes** — under M1 the engine answers `DISJOINT`, not a collision, so that leg's forbidden outcome genuinely did not occur. **The missing kill was never a refutation.** |
| M2 | KILLED by 8 | **KILLED by 8** | same count; **2 of the 8 moved from `NOT MEASURED` to `FAIL`** |
| M3 | KILLED by 5 | **KILLED by 5** | same count; **the leg that found the whole defect moved from `NOT MEASURED` to `FAIL`** |
| M4 | KILLED by 1 | **KILLED by 1** | unchanged; the thinness is real and re-confirmed |

### The claim `#1825` asked to be confirmed rather than assumed

> the four KILLED verdicts stand under either anchor … what the defect degraded was **diagnostic
> quality, not coverage**.

**Confirmed, and it is now a number rather than an argument.** All four still kill. What changed is
that **three previously-`NOT MEASURED` results became real `FAIL`s** (M2×2, M3×1) and **one spurious
kill disappeared** (M1's fifth, which was a non-measurement being counted as a refutation). Coverage
identical; diagnosis correct.

That last one is worth stating plainly, because it cuts slightly against the original claim: the
guard-produced anchor did not only *degrade* diagnosis, it also **inflated one kill count by one**. A
`NOT MEASURED` counted as a kill is a kill you do not have.

### Two legs are defended by exactly one check each

**M4** (`body: null` stays readable) and **M8** (the off-board sweep's unreadable-body arm) each die to
a single unit test and **no parity leg at all**. `#1794` flagged M4's thinness; M8's is new here and is
the same shape. Neither is a defect — a guard with one honest test is tested — but a single test is one
rename away from zero, and both now have a number rather than an impression.

**M5 and M6 are the mirror image**: 10 parity kills each and **zero unit coverage**. The collision
scan's fail-closed behaviour is defended entirely by the HTTP corpus, which is consistent with
`#1794`'s reasoning that `Fake.Recorder` sits on the wrong side of `Transport.Send` — but it means
those guards depend on a fixture, not on a type.

## The anchor defect, and the rule that generalises

`#1794`'s `refute` legs anchored on `"could not be read"` — **the text the guard itself emits** — so
reverting the guard removed the anchor and the leg reported `NOT MEASURED` exactly where it should have
reported `FAIL`. `#1808` records the rule:

> **The anchor must prove the command RAN, not that the guard FIRED.**

`#1825` prescribed the subject's ref, `FS.GG.SDD#501`. Measured against real engine output, that is
right for five of the seven anchor uses and **wrong for two**: the unidentifiable-element refusal is
raised inside `Reads.openIssues`, one layer *below* the collision scan, so it names
`FS-GG/FS.GG.SDD open issues` and never the ref. Those two use an alternation. The statable form, which
is what makes the rule checkable rather than a matter of taste:

> **The anchor must match every verdict the command can produce, and nothing it produces when it did
> not run.**

Auditing all seven against that rule — not just the one `#1825` named — **every one of `#1794`'s seven
anchor uses violated it**: `FS.GG.SDD#503`, `collides with`, `could not be read` (×3) and
`element 2 of 4` (×2) are all text some guard produces.

## M10 is the leg that proves the harness can fail

Breaking `openissuespage_server.py` so it cannot bind produced **7 parity legs reporting
`NOT MEASURED`** — not `PASS`, not `FAIL` — alongside 14 ordinary failures. Under the raw
`case … *) ok` shape `#1794` replaced, those 7 would have reported **PASS**, because the forbidden
string is absent from an output that does not exist. That is specification point 4 (`#1808`) and the
three-valued outcome demonstrated end to end rather than asserted.

## What this does not say

Ten guards fire under mutation. That is not a claim that they guard the right things, that the corpus
is complete, or that anything else in `tests/coord-engine-parity/run.sh` is sound — **26 absence-shaped
assertions there still conclude a PASS from an absence without going through `refute`** (`#1849`).

