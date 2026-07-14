namespace FS.GG.Coord.GitHub

/// The RECORDING FAKE — the other half of the corpus's black-box character.
///
/// ADR-0040 C1 is the reason this exists, and it is worth restating, because it is the constraint that
/// shapes the whole port:
///
/// > The 880-assertion corpus drives `bash scripts/fsgg-coord` against a **PATH-shim `gh` stub that counts
/// > calls**. That stub is how every budget assertion works, every ETag-304 assertion, every fail-closed
/// > assertion. An F# tool calling `HttpClient` directly is **invisible to it** — and the corpus, which
/// > ADR-0038 made the cut-over gate, would die at the exact moment it is most needed.
///
/// So this fake sits at the transport seam and reproduces, exactly, what the `gh` stub observes:
///
/// 1. **The call counters.** One line per call, split GraphQL / REST. `GET /rate_limit` is counted by
///    NEITHER — the meter read is free, and the corpus's `bootstrap: 2 GraphQL calls` (and every count
///    delta after it) shifts by one if it is billed. A FAILED call still counts: the stub increments
///    before it injects its 403, because a call you were charged for is a call you made.
///
/// 2. **`$GH_LOG`, in the stub's own grammar.** This is the part C1 implies but does not name, and it is
///    load-bearing. Roughly twenty corpus assertions grep for `gh project item-edit`'s CLI FLAGS —
///    `--single-select-option-id opt_p2`, `--field-id PVTSSF_phase`, `--clear` — and those bytes exist in
///    no HTTP request anywhere. Under the port that write becomes a GraphQL mutation, so unless the fake
///    SYNTHESISES the old line from the typed request, those assertions do not fail: they simply stop
///    being about anything, which is worse. Emitting the stub's grammar keeps them true statements about
///    the new code, and keeps the corpus diff at zero.
///
/// A fake that logged its own convenient shape would be a fake that agrees with itself. That is the
/// failure this whole project is about.
module Fake =

    open Errors
    open Transport

    /// How the fake answers one request. Supplied by the test — this is the fixture table, and the fault
    /// injector, and nothing else.
    ///
    /// Returning an `IoResult` (not a `Response`) is what lets a test inject a TRANSPORT failure — a reset
    /// connection, a timeout — which is a different fact from an HTTP error and must be testable as one.
    type Route = Request -> IoResult<Response>

    /// A transport that records everything and decides nothing.
    type Recorder =
        new: route: Route -> Recorder

        interface IGitHubTransport

        /// Calls billed to the GraphQL meter. `GET /rate_limit` is not among them.
        member GraphQlCalls: int

        /// Calls billed to the REST meter.
        member RestCalls: int

        /// The log, in the `gh` stub's grammar — `issue-get FS-GG/FS.GG.SDD 42`, `comment-post …`,
        /// `item-edit --id … --field-id … --single-select-option-id …`, `batch-mutation mutation {…`.
        member Log: string list

        /// Does the log contain this needle? The corpus's `assert_contains` over `$GH_LOG`, as a method.
        member Logged: needle: string -> bool

        /// How many log lines contain this needle? The corpus's `grep -c`, as a method.
        member Count: needle: string -> int

    /// Render one request in the `gh` stub's log grammar.
    ///
    /// Exposed because it IS the contract, and a contract that only its own implementation can see is one
    /// nobody can check. The corpus's needles are asserted against this function directly, so that a
    /// change to the grammar breaks a test here rather than silently hollowing out twenty assertions over
    /// in the shell.
    val describe: request: Request -> string
