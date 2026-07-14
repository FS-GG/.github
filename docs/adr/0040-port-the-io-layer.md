# ADR-0040: The IO layer is ported too — "delete the bash implementation" was never executable, and the half that was left behind is where the bugs still are

- **Status:** Proposed
- **Date:** 2026-07-14
- **Affects:** `.github` (the engine, the client, the kit row, ten workflows), and every `receives: coordination-kit` repo — sdd, rendering, governance, templates, game, audio
- **Amends:** [ADR-0034](0034-typed-coordination-engine.md) §5 (the exit criterion) and §4.4 (the shim's preconditions). ADR-0034's *decision* stands entirely; this is about what it takes to finish it.
- **Depends on:** [#750](https://github.com/FS-GG/.github/issues/750) — **hard prerequisite**, see C3.

## Context

[ADR-0034](0034-typed-coordination-engine.md) §5 ends:

> `--engine=fs` becomes the default; `--engine=bash` remains as the escape hatch.
> **One week later, delete the bash implementation.** The kit row becomes the shim (§4.4).

**That cannot be executed, and it is not a scheduling problem.**

The typed engine has **zero IO**. Its only package reference is `FSharp.Core` — no `HttpClient`, no
`Octokit`, no `Process.Start`. Its own usage text says so: *"It is pure: it reads NOTHING — no board, no
issues, no network, no GitHub token."* It takes a JSON snapshot on stdin and prints a verdict.

`scripts/fsgg-coord` makes **53 `gh` calls** across **47 commands** and performs **every write**: the
claim CAS, the board field writes, `child`, `done`, `widen`, `release`, `heartbeat`, `say`, the PR
creation, the done-stamp.

So deleting bash would delete the only thing that can read a board or post a claim marker, and §4.4's
shim would resolve a binary that cannot do the job. The step is not merely hard. **It is not reachable
from where we are.**

**This is the third time.** [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) §3
obliged a change that *"cannot exist"* — the schema doc is in `.github`, the validator is in FS.GG.SDD,
and no PR spans two repos; [ADR-0037](0037-schema-growth-is-publish-before-flip.md) fixed it.
ADR-0034 §5's three-day fleet clock could never tick, because a worker in a per-item worktree resolves
no engine and banks no evidence ([#728](https://github.com/FS-GG/.github/issues/728));
[ADR-0038](0038-the-corpus-is-the-cut-over-gate.md) fixed it. And now §5's *delete*. Three accepted
decisions whose next step no plan could execute. That is a pattern in how this org writes decisions, and
it is worth naming out loud: **a decision that does not name its preconditions will produce a plan that
stops.**

### It also explains the board, and corrects a claim we have been making

Epic [#729](https://github.com/FS-GG/.github/issues/729) says the port *"retires 22 of 40 open issues"*,
and 26 issues carry the `engine-retires` label. **Both are wrong**, and the flip is what made it visible.

The engine retires **one family**: the schedulability predicate — [#669](https://github.com/FS-GG/.github/issues/669)
and parts of #636, #646, #651. Call it four to six.

The rest — roughly **nineteen** — are in the half that was never ported:

| issue | what it is |
|---|---|
| [#706](https://github.com/FS-GG/.github/issues/706) | `widen` never checks the caller **holds** the claim — a live holder's touch-set was rewritten |
| [#523](https://github.com/FS-GG/.github/issues/523) | `widen` **PATCHes** the declaration before it re-checks it |
| [#614](https://github.com/FS-GG/.github/issues/614) | `done --flip` **closed an open parent** whose only child was a partial fix |
| [#613](https://github.com/FS-GG/.github/issues/613) | `epic_rollup` stamps a parent Done but never **closes** it |
| [#584](https://github.com/FS-GG/.github/issues/584) | the claim scan **fails open** on a transient read |
| [#507](https://github.com/FS-GG/.github/issues/507) | one issue body blows the **arg cap** — state travelling through `argv` |
| [#585](https://github.com/FS-GG/.github/issues/585) | `take` exits **0** when it claimed nothing |
| … | and a dozen more of the same shape |

Every one is a **write-path or IO** defect. Every one survives the flip untouched. Every one is exactly
what ADR-0034 §2 said the substrate makes cheap — *"an error, an empty result, and a legitimate 'no' are
the same value"* — and they are still arriving, because **the substrate that produces them is still
there.**

Porting the decision core and stopping was never going to close them. It was only ever going to make
them easier to see.

## Decision

**The IO layer is ported to F#. `scripts/fsgg-coord` becomes the shim of ADR-0034 §4.4 — and only once
the four preconditions below actually hold.**

### C1 — The corpus is the asset, and `HttpClient` is invisible to it

The 880-assertion corpus drives `bash scripts/fsgg-coord` against a **PATH-shim `gh` stub that counts
calls**. That stub is how every budget assertion works, every ETag-304 assertion, every fail-closed
assertion. An F# tool calling `HttpClient` directly is **invisible to it**, and the corpus —
which ADR-0038 made the cut-over gate — would die at the exact moment it is most needed.

**So the IO layer is a PORT, not a set of call sites.** An `IGitHub` interface with two implementations:
an HTTP adapter, and a **recording fake** that counts calls exactly as the stub does. The shell corpus
drives the tool through a configurable API base so it keeps its **black-box** character — it tests the
tool, not its units, which is the whole reason it caught what the unit tests did not.

> **No step of this port may land that reduces the corpus.** If a change cannot be expressed with the
> corpus still green, that is information about the change.

### C2 — The kit row runs where there is no .NET

`touch-set-drift.yml` runs `bash scripts/fsgg-coord verify-paths` on `ubuntu-latest` with
`actions/checkout@v7` and **no `setup-dotnet` step**. **Ten** workflows shell out to the client. A
compiled binary breaks all of them — silently, in six repos. ADR-0034 §4.4 names this and then does not
schedule it.

**Every workflow that shells out gains `setup-dotnet` + `dotnet tool restore` BEFORE the shim lands, not
with it.** That is an independently-landable PR whose only job is to be boring, and it must be green in
all six receivers before the shim is cut.

### C3 — The shim presumes the tool is restorable, and nothing guarantees it

The shim resolves `fs.gg.coord.cli` from `.config/dotnet-tools.json`. Per
[ADR-0039](0039-nuget-org-is-the-read-path.md) §1, **five of six receivers restore from nuget.org**. That
publish is gated behind `vars.NUGET_ORG_PUBLISH` — a repo variable **nothing asserts**
([#750](https://github.com/FS-GG/.github/issues/750)).

So the shim's central premise — *the tool is there* — currently rests on a variable nobody checks. Cut
the shim before that gate exists, and **one unset variable silently un-tools six repos**, as an empty
version list rather than an error.

**#750 is a hard prerequisite of this port, not an adjacent nicety.**

### C4 — The lock does not move to the budget that dies first

ADR-0034 is right and still binds: the comment-order CAS lives on **REST** *deliberately*, because
GraphQL is the first budget to die under fan-out ([#418](https://github.com/FS-GG/.github/issues/418)),
and **a lock may never live on the budget that dies first.** This port changes the *language*, not the
*substrate*. The CAS is re-expressed, never re-designed.

## The staging — and every step is reachable from the one before it

That property is the whole point of this ADR, so it is stated as a rule: **no phase may name an exit
criterion that the phase before it cannot produce.**

**Phase A — the read path, behind bash.** `IGitHub` + the HTTP adapter + the recording fake. The board
scan, the issue reads, the marker reads, the ETag cache, the budget meter.
**Exit:** the corpus is green driving the F# read path through the fake, with the call counts unchanged.

**Phase B — the write path.** The CAS, `set-field`, `child`, `done`, `widen`, `release`, `heartbeat`,
`say`. Each write becomes a function with a **precondition in its type** — and that is where the nineteen
issues die: `widen` without an ownership check is not *expressible* if the operation takes a held claim
as its argument (#706), and a PATCH that precedes its re-check is not expressible if the re-check
produces the value the PATCH consumes (#523).
**Exit:** the corpus is green; and every `engine-retires` issue either has a passing case in it, or is
**re-labelled honestly**. The label is wrong today and this is where it stops being wrong.

**Phase C — the preconditions.** #750's gate. `setup-dotnet` in all ten workflows, across six receivers.
**Exit:** both green on `main`, on their own, with no shim in sight.

**Phase D — the shim, and the deletion.** `scripts/fsgg-coord` becomes the ~40-line resolver of §4.4. The
`kind: client` row still digests, still byte-copies, still byte-compares — none of that machinery
changes, which is why Option D was chosen.
**Exit:** the corpus is green **through the shim**, in all six receivers. `--engine=bash` is removed
because there is no bash left to be.

## The exit criterion, stated so it can be met

> **Bash is deleted when the corpus is green through the shim in all six receivers, with the restore
> gate green.**

Not *"one week later"*. ADR-0034 §5 named a **date**, and a date is not a criterion — it is a hope with a
calendar attached. This is the same correction ADR-0038 made to the three-day clock, and it is being made
for the same reason: **an exit criterion that no one can compute will be met by someone deciding it has
been.**

## Consequences

- **ADR-0034 §5's "one week later, delete the bash implementation" is superseded.** §5's *direction* was
  right; its *schedule* was a wish and its *preconditions* were unstated.
- **The `engine-retires` label is wrong and must be re-derived.** ~19 of 26 are Phase B, not Phase 0.
  Leaving it is worse than a wrong label: it tells every worker that nineteen live defects are already
  fixed by work that has landed.
- **Epic #729's "retires 22 of 40" is overstated.** The flip retired four to six. This port retires the
  rest — that is what it is *for*, and saying so honestly is the only way the number ever becomes true.
- **This is a large port**, and the corpus is what makes it survivable. It is also the argument for doing
  it now rather than in six months: the corpus exists *today*, it is green against both engines *today*,
  and every week bash survives it accrues more defects that the corpus must then also encode.

## Alternatives considered

**Keep bash for IO permanently, and fix the nineteen in bash.** Rejected — it is the treadmill. It
retains the substrate whose default is fail-open, which ADR-0034 §2 already established as the *cause*
rather than the symptom. Those nineteen would be fixed, and the twentieth would arrive.

**Rewrite from scratch.** Rejected, again ([ADR-0038](0038-the-corpus-is-the-cut-over-gate.md) rejected
it once). The typed core is the asset here; ~2,500 lines of it are correct and proven against 880
assertions. The IO layer is *additive* to it, not a reason to discard it.

**Port the IO to F# but keep shelling out to `gh`.** Tempting — it preserves the PATH-shim stub for free,
and keeps `gh`'s auth handling. Rejected: it keeps a subprocess boundary on the hot path, keeps state
travelling through `argv` (#507 is *literally that bug*), and leaves every response as untyped text —
which is the substrate complaint one layer in, wearing a different hat.
