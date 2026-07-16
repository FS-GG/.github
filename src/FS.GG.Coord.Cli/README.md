# FS.GG.Coord.Cli — the typed coordination engine

The schedulability model behind `fsgg-coord`, as **one total function**.

This is not a user-facing tool. `scripts/fsgg-coord` is the client you run; this is the engine it
shells out to. You will normally never invoke it by hand.

## What it is for

`scripts/fsgg-coord` was 4,000 lines of bash modelling a concurrent, transactional, budget-constrained
domain in a substrate with no types, no `Result`, no atomicity, and whose default failure mode was to
**fail open** — an error, an empty result, and a legitimate "no" were the same value. The defect record
follows from the substrate: *"is this item startable?"* was computed in five places and agreed in none
([#485](https://github.com/FS-GG/.github/issues/485)), and the fail-open family
([#266](https://github.com/FS-GG/.github/issues/266)) has 51 children.

[ADR-0034](https://github.com/FS-GG/.github/blob/main/docs/adr/0034-typed-coordination-engine.md) moves
that domain to a typed F# core. There is no `bool` in a verdict:

```fsharp
type Verdict<'a> = Green of 'a | Red of string list | NoVerdict of reason: string

type Schedulability =
    | Startable
    | WrongStatus of BoardStatus        // NoStatus is its own case, not a Backlog
    | IssueClosed                       // the issue outranks the board column
    | NoTouchSet                        // an OMISSION
    | DeliberatelyNoTouchSet            // `Paths: none` — a DECISION. Not the same fact.
    | UnusableTouchSet of tokens: string list
    | BlockedBy of Blocker list         // resolved = CLOSED *or MERGED*
    | HeldBy of WorkerId
    | HeldByLiveWork of WorkerId * pr: int   // the lease lapsed; the WORK did not
    | OverlapsInFlight of (string * string) list
    | Undetermined of reason: string    // "I could not decide." NEVER green, never a silent skip.
```

## It reads nothing

The engine performs **no IO**. No board, no issues, no network, no token. The client has already paid
for the board scan and the claim markers by the time it decides, so it hands that state over on stdin
and the engine decides from it:

```sh
fsgg-coord-engine decide < snapshot.json      # → a typed verdict per candidate, as JSON
fsgg-coord-engine decide --text < snapshot.json
```

That is what makes the engine **pure and reproducible**: the verdict is a total function of the snapshot,
so the same board state always decides the same way, and a test can *state* the answer rather than stand a
live fixture up — no second scan of the 5,000 pt/hr budget the whole fleet shares
([#418](https://github.com/FS-GG/.github/issues/418)).

## It is the client now

`scripts/fsgg-coord` is the [ADR-0034](https://github.com/FS-GG/.github/blob/main/docs/adr/0034-typed-coordination-engine.md)
§4.4 shim ([ADR-0040](https://github.com/FS-GG/.github/blob/main/docs/adr/0040-port-the-io-layer.md) Phase D):
it resolves this compiled engine and `exec`s it, passing argv through unchanged. The ~7,000-line bash
implementation, and the *shadow* that once ran both engines side by side and returned bash's answer, are
gone — this engine is the one and only coordination client.

## Exit codes

These are the **engine's**, not the client's — the client translates them.

| Code | Meaning |
|---|---|
| `0` | green — a batch was computed |
| `1` | bad arguments, or a malformed snapshot |
| `2` | the engine itself broke (a defect, never the caller's fault) |
| `3` | **red** — the batch is refused. A reservation whose touch-set is unmatchable reserves *nothing*, so scheduling against it would hand a second worker files somebody is standing in. |
| `4` | **no-verdict** — could not reach an answer. Never zero, and never silently a "no". |

An unreachable answer is not a negative one. That rule is the whole point.

## License

MIT
