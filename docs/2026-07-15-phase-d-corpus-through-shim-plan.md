# Phase D — the corpus through the shim, and the deletion of bash

**Date:** 2026-07-15
**Owner:** `.github` (the coordination engine)
**Governs:** the execution of [ADR-0040](adr/0040-port-the-io-layer.md) Phase D
**Status:** Plan — not yet started. Phases A–C have landed; this is the last phase.

---

## 1. Where we are

[ADR-0040](adr/0040-port-the-io-layer.md) ports the coordination engine's IO layer to F# and then
makes `scripts/fsgg-coord` the ~40-line **shim** of ADR-0034 §4.4 that execs the compiled tool. It stages
the work A→D, "each step reachable from the one before it." A, B, and C have landed:

- **Phase A/B (read + write path)** — the `IGitHub` seam, the HTTP adapter, the recording fake, the CAS,
  the board writes, `done`, `widen`, `child`, `set-field`, `release`, `heartbeat`, `say`. The engine
  reads its own board and performs every write over HTTP.
- **Phase C (preconditions)** — `setup-dotnet` + `dotnet tool restore` in the workflows that shell out
  ([#770](https://github.com/FS-GG/.github/issues/770)), and the `NUGET_ORG_PUBLISH` restore gate
  ([#750](https://github.com/FS-GG/.github/issues/750), [#765](https://github.com/FS-GG/.github/issues/765)).

**The engine is now proven case-by-case, over HTTP, against the corpus's certified answers.** The
`tests/coord-engine-parity/` harness (52 assertions across ~11 corpus cases) drives the *compiled binary*
against fixture GitHub servers and holds it to the exact answers the shell corpus certifies for bash —
scheduling, blockers, starved-vs-empty, cross-repo scoping, fail-closed reads, touch-set fabrication,
one-item-per-worker, and the full `take` exit-code contract. Two real write-path defects the port was
*for* have been closed in the engine along the way: [#516](https://github.com/FS-GG/.github/issues/516)
(one item per worker) and [#585](https://github.com/FS-GG/.github/issues/585) (distinct `take` exit
codes). The rest of ADR-0040's "~19" are either addressed in source or closed by construction (a typed
`Result` makes a failed read an `Error` at every call site — [#584](https://github.com/FS-GG/.github/issues/584)
cannot exist in the engine).

**What has NOT happened:** the *full* corpus (29 cases, 891 assertions) still drives **`bash`
scripts/fsgg-coord** through a PATH-shim `gh` stub. The engine shadows it (`50-shadow-engine`) and is
compared against it (`51-fs-flip`), but bash is still the thing under test, and bash still exists.

Phase D closes that.

## 2. The exit criterion (from ADR-0040)

> **Bash is deleted when the corpus is green through the shim in all six receivers, with the restore
> gate green.**

Not a date. A computable condition. Three obligations inside it:

1. The **corpus runs green through the shim** — i.e. against the *engine*, not bash.
2. It does so in **all six receivers** (sdd, rendering, governance, templates, game, audio).
3. The **`NUGET_ORG_PUBLISH` restore gate** is green (done — C3).

## 3. The one hard problem: the corpus counts `gh`, the engine speaks HTTP

This is the crux ADR-0040 C1 names, and it is the whole of the technical work.

The 891-assertion corpus is a **black box** over `bash scripts/fsgg-coord`, driven against a **PATH-shim
`gh` stub that counts calls**. Every budget assertion ("this operation costs N GraphQL points"), every
ETag-304 assertion, every fail-closed assertion works by counting or faulting `gh` invocations. **An F#
tool calling `HttpClient` directly is invisible to that stub** — it makes zero `gh` calls — so a corpus
that simply pointed `scripts/fsgg-coord` at the shim would see every call-count collapse to zero and die
at the moment it is most needed.

ADR-0040 C1's resolution: the corpus keeps its black-box character by driving the tool **through a
configurable API base**, with the call-counting moved from the `gh` stub to the **HTTP layer** (a fixture
server that counts requests, or the recording fake). This is a **transformation of the fixture layer, not
a reduction of the assertions** — the property ("costs N GraphQL calls") is still checked; it is counted
one transport over.

**The `tests/coord-engine-parity/` harness is the working prototype of exactly this.** It already drives
the compiled engine against stdlib HTTP fixture servers (`pw_server.py`, `starved_server.py`,
`ratelimit_server.py`, …), counts and faults at the HTTP level (the malformed-marker toggle, the 403
rate-limit, the ETag-capable transport), and holds the engine to the corpus's certified answers. Phase D
is, in essence, **growing that prototype to cover the full corpus** — or, equivalently, teaching the
existing corpus harness to drive the engine through a configurable API base.

## 4. Preconditions (ADR-0040 C1–C4) — status

| # | precondition | status |
|---|---|---|
| **C1** | no step may *reduce* the corpus; the IO layer is a PORT with an `IGitHub` seam + recording fake; drive through a configurable API base | seam + fake landed (A/B); the **configurable-API-base corpus** is the work of §5 below |
| **C2** | the kit row runs where there is no .NET — `setup-dotnet` in every workflow that shells out, green in all six receivers, *before* the shim | **done** ([#770](https://github.com/FS-GG/.github/issues/770)) |
| **C3** | the shim presumes the tool is restorable — the `NUGET_ORG_PUBLISH` gate must exist | **done** ([#750](https://github.com/FS-GG/.github/issues/750)/[#765](https://github.com/FS-GG/.github/issues/765)) |
| **C4** | the lock stays on REST (GraphQL dies first under fan-out) | held — the CAS was re-expressed on REST in Phase B, not re-designed |

## 5. The staged plan — each step reachable from the one before it

### D.1 — Drive the FULL corpus through the engine, locally, green

Grow the parity prototype into a **full corpus-through-engine harness**: every one of the 29 cases'
certified answers, produced by the *engine* over HTTP against a fixture server, with the budget/ETag/
fail-closed assertions re-expressed at the HTTP layer.

- Prefer **reusing the shell corpus verbatim** by giving it a configurable API base and an HTTP-level
  counting fixture (the `gh` stub becomes an HTTP fixture; `run` points `scripts/fsgg-coord` — under the
  shim — at it). This keeps the 891 assertions *as they are* and honours C1's "no reduction".
- Where an assertion counts `gh` invocations specifically, re-express it as an HTTP request count. Log,
  do not silently drop, any assertion that genuinely has no HTTP-level form.
- **Exit:** the corpus is green driving the engine (through the shim) locally, with call counts intact,
  and `50-shadow-engine` / `51-fs-flip` still green (bash still present, still agreeing).

### D.2 — Cut the shim

Replace `scripts/fsgg-coord` with the ~40-line resolver of ADR-0034 §4.4: resolve `fs.gg.coord.cli` from
`.config/dotnet-tools.json`, exec it, pass through args and exit code. The `kind: client` kit row still
digests, still byte-copies, still byte-compares — none of that machinery changes (why Option D was
chosen).

- **Exit:** the corpus (D.1) is green *through the shim* on `.github@main`; every workflow that shells
  out is green (C2); the restore gate is green (C3).

### D.3 — Green in all six receivers

Roll the shim to the six `receives: coordination-kit` repos via the existing digest → byte-copy →
byte-compare fabric. No receiver edits — the shim and its corpus are distributed like every other kit
artifact.

- **Exit:** the corpus is green through the shim in **all six receivers**.

### D.4 — Delete bash, dispose the five differential assertions on the record

Delete the ~4,000 lines of `bash scripts/fsgg-coord`. `--engine=bash` is removed *because there is no
bash left to be*. Per ADR-0040's "Phase D contradiction, and its resolution", the five `51-fs-flip.sh`
differential assertions are **retired on the record** — not silently dropped:

| # | assertion | disposition |
|---|---|---|
| 1–2 | `fs` returns bash's items / same exit code | **subsumed** by the ADR-0038 defect-corpus-against-`fs` (a precondition of D.1) |
| 3–5 | `--engine=bash` is byte-exact / never consults the engine | **retired** — the escape hatch is the thing being deleted |

Land a `51-fs-flip.sh` (or sibling manifest) that **records** the five and their disposition, so the drop
is a decision in the diff, reviewable, never a silent gap. **A silently shrinking gate is the failure; a
documented retirement is not.**

- **Exit:** bash is gone; the corpus is green through the shim in all six receivers; the disposition
  manifest is on the record.

## 6. Risks and rollback

- **The configurable-API-base corpus is the risk.** If an assertion genuinely cannot be expressed at the
  HTTP layer, that is *information about the assertion* (C1) — surface it, do not drop it. The parity
  harness proves the shape is achievable; the risk is breadth, not feasibility.
- **Rollback is per-phase.** D.1 adds a harness and changes nothing shipped. D.2's shim is revertible
  (restore the bash file) *until D.4*. **D.4 is the one-way door** — it is taken only after D.1–D.3 are
  green in all six receivers, which is the whole point of gating it on a computable condition rather than
  a calendar.
- **The lock must not move to the budget that dies first (C4).** Any temptation during D.1 to satisfy a
  budget assertion by moving a REST read onto GraphQL is the exact regression ADR-0034 forbids.

## 7. Definition of done

- [ ] D.1 — the full corpus green through the engine locally, call counts intact, shadow/flip still green.
- [ ] D.2 — the shim cut; corpus green through it on `.github@main`; C2 + C3 green.
- [ ] D.3 — green through the shim in all six receivers.
- [ ] D.4 — bash deleted; `--engine=bash` removed; the five `51-fs-flip.sh` assertions disposed of on the
      record; the `engine-retires` label and epic [#729](https://github.com/FS-GG/.github/issues/729)'s
      "retires 22 of 40" re-derived honestly (ADR-0040 Consequences).

---

*This plan executes ADR-0040 Phase D. It does not amend it. Where the two differ, the ADR governs.*
