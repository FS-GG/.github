# ADR-0046: One exit-code union, and the two GitHub-layer codes move off the verdict codes they collided with

- **Status:** Accepted
- **Date:** 2026-07-17
- **Affects:** FS-GG/.github (the `fsgg-coord` engine's process exit codes); every caller that reads them — `/pnext-item`, `/check-board`, the `fsgg-coord` shim, and any worker loop that branches on an exit code

> The verdict-code literals settled during the bash→engine cutover (ADR-0034 §5, ADR-0040) — `3 == red`, `4 == no-verdict`, `7 == pending` — are **untouched**. This ADR moves only the two GitHub-layer codes off the verdict codes they collided with, and consolidates the declaration; it does not re-open a verdict-code clause.

## Context

The engine's process exit codes were `[<Literal>] let` ints declared in **three** modules, and two numbers carried two meanings each ([.github#918](https://github.com/FS-GG/.github/issues/918)):

| site | declared |
|---|---|
| `src/FS.GG.Coord.GitHub/Errors.fs` | `ExRate = 75`, `ExOffboard = 3`, `ExPartial = 4` |
| `src/FS.GG.Coord.Cli/Client.fs` | `ExitGreen = 0`, `ExitError = 1`, `ExitRed = 3`, `ExitNoVerdict = 4`, `ExitNone = 5`, `ExitContended = 6`, `ExitPending = 7` |
| `src/FS.GG.Coord.Cli/Program.fs` | a `private`, verbatim re-declaration of a subset of `Client.fs` — a fourth copy the compiler could not even compare |

Two collisions:

- **`3`** = `Errors.ExOffboard` ("this issue is not an item on the board") **and** `Client.ExitRed` ("the verdict is red").
- **`4`** = `Errors.ExPartial` ("a `set-field --batch` write half-landed") **and** `Client.ExitNoVerdict` ("no verdict could be reached — fail closed").

These are not near-synonyms. `ExOffboard` is a fact discovered on a **successful** read; `ExitRed` is a **verdict**. `ExPartial` is a write **outcome**; `ExitNoVerdict` is the **absence** of an answer. `Errors.fs` already argues at length that `NotFound` must not collapse into `ExOffboard` because collapsing distinct meanings is how [#421](https://github.com/FS-GG/.github/issues/421) happened — the same argument applies to the collision it sat next to.

Because the codes were ints threaded through three modules rather than one type, **nothing could enumerate what a command returns.** The generated `take`/`landable` exit-code tables (`Protocol.takeExitCodes`/`landableExitCodes`, projected into `/pnext-item`) were therefore hand-derived, and their completeness could only be proof-read. That is exactly how the first draft of `takeExitCodes` shipped **with no row for `ExitRed`** — reachable via `renderDecision`'s `Red` arm — caught only by a human reading `take` line by line ([#916](https://github.com/FS-GG/.github/issues/916)). `ExitContractTests` (#889) could pin doc↔**constant**, but not doc↔**behaviour**, and it had to key on the *name* `EX_PARTIAL` rather than the number `4` precisely because `4` was ambiguous — a test contorted around the defect in the thing it tested.

The decision was escalated (it is a change to caller contracts, not a mechanical fix) and made by the org owner on #918.

## Decision

**1. One union.** `FS.GG.Coord.ExitCode` (new, in `Core`) declares every code the engine can return as a case, with a single `toInt` — the one place a case becomes a number. `Errors`, `Client`, and `Program` derive their constants from it; `Program.fs`'s private re-declaration is deleted. A collision now means two cases mapping to one number in `toInt`, visible in one place a reviewer reads.

**2. The colliding meanings are DISTINCT, so one of each pair moves.** They do not merge. The **verdict codes keep their numbers** — `3 == red` is load-bearing across every verdict command and `4 == no-verdict` is `landable`'s fail-closed contract; these are the numbers callers poll on. The two **GitHub-layer** codes move to the lowest free numbers:

- `ExOffboard: 3 → 8`
- `ExPartial: 4 → 9`

**3. Shared base + per-command.** The union carries the common codes plus the per-command ones (`take`'s `5`/`6`, `landable`'s `7`), matching the engine help's existing "generic table + per-command overrides" rendering. `ExitCode.takeCodes` / `ExitCode.landableCodes` make a command's return set a **value**, so `Core.Tests` checks each projected table **complete** against it — the half #916 could not build. What remains hand-derived is the domain list itself: the handlers still return `int`, not `ExitCode`, so the compiler cannot yet force `takeCodes` to equal the arms `Client.take` reaches. Making that total (handlers returning the union) is a further refactor, deferred.

## Consequences

- **This diverges from the frozen bash corpus for two codes, deliberately.** The corpus (`tests/coord-engine-parity`) certified bash's `EX_PARTIAL = 4`; the engine now returns `9`. Bash is retired (`scripts/fsgg-coord` is a shim), so the engine is authoritative and the parity assertion is updated to `9` with this ADR cited. This is the "renumbering re-opens a disposition" #918 named — resolved here, on the record.
- **Callers that branch on `8`/`9` must be updated.** Within this repo: `docs/design/coordination-engine.md` (the worker-loop contract table) is updated `3 → 8`. The generated `take`/`landable` tables are **unaffected** — neither documents the GitHub-layer codes. Downstream skills/shim consumers that special-case `EX_OFFBOARD`/`EX_PARTIAL` by number must move to `8`/`9`.
- **`[<Literal>]` is dropped from the derived constants.** Nothing pattern-matches or attributes them (verified: only comparisons and returns), so deriving them from `toInt` is safe; the `.fsi` signatures lose their `= N` literal values.
- **The tests get stronger.** `ExitContractTests` now pins every module's constant to the union (`every module's exit constant is the union's number`) and pins the previously digit-only `landable` defect row to `ExitCode.toInt ExitCode.Defect`.

## Alternatives considered

- **Collapse the collisions (declare off-board a kind of red, partial a kind of no-verdict).** Rejected: it asserts an equivalence the `Errors.fs`/#421 argument refutes — a successful-read fact is not a verdict, a half-landed write is not an absent answer.
- **Keep three int sites, add only a cross-check test.** Rejected: it leaves the numbers in three places and cannot make a command's return set enumerable — the completeness gate #918 exists to enable has nothing to check against.
- **Make the handlers return `ExitCode` now (full behavioural totality).** Deferred, not rejected: it is the right end state, but it is a larger refactor across `Cli`/`GitHub`, and a live claim held `Client.fs` when this landed.
