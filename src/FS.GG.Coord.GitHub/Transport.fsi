namespace FS.GG.Coord.GitHub

/// The wire. One request in, one `IoResult<Response>` out — and NOTHING above this line knows about HTTP.
///
/// THE SEAM IS HERE BECAUSE THE CORPUS COUNTS HERE. The cut-over gate (ADR-0040 C1) is a call-counting
/// corpus: *"no step of this port may land that reduces the corpus"*. The bash corpus counted `gh`
/// invocations at a PATH-shim stub; the port re-expressed that one transport under, and `tests/coord-engine-parity/`
/// now counts HTTP requests at the fixture (bash and its stub were deleted in ADR-0040 D.4). A tool that
/// reached for `HttpClient` inline would be INVISIBLE to either — every budget assertion, every ETag-304
/// assertion, every fail-closed assertion is written against a request counter, and a direct call is a
/// call nobody counted.
///
/// So the transport is an INTERFACE with two implementations: `HttpTransport` (the real one, pointed at a
/// configurable API base) and `Fake` (the recording one, which counts calls exactly as the stub does and
/// re-emits its log in the stub's own grammar). The corpus keeps its BLACK-BOX character — it tests the
/// tool, not its units, which is the whole reason it caught what the unit tests did not.
module Transport =

    open Errors

    /// Which budget a call spends. They are separate meters and they are not interchangeable.
    type Budget =
        /// The 5,000 pt/hr GraphQL budget — metered by NODES, shared by the fleet, and the first to die
        /// under fan-out (#418).
        | GraphQl
        /// The 5,000 req/hr REST budget — metered by REQUESTS, one point each, and 304s are free. The lock
        /// lives here DELIBERATELY: a lock may never live on the budget that dies first.
        | Rest
        /// `GET /rate_limit`. The meter read does not itself spend the meter — which is exactly what makes
        /// "back off until the reset" a strategy rather than a guess.
        ///
        /// It is counted by NEITHER counter, and the corpus depends on that: `bootstrap: 2 GraphQL calls`
        /// and every count delta after it shift by one if a meter read is billed.
        | Free

    /// A GraphQL variable, WITH ITS TYPE.
    ///
    /// Not a string. GraphQL is typed, and Projects v2's field mutations are where that stops being a
    /// formality: a NUMBER field wants `{"number": 42}` and rejects `{"number": "42"}`. Serialising every
    /// variable as a string is the kind of shortcut that works for every document you have and fails on the
    /// first one you write — so the type is carried, and the JSON is derived from it.
    /// The tag picks the type the variable is DECLARED as, and GraphQL validates that declaration against
    /// the argument's schema type without ever looking at the value. So a tag is a claim about the SCHEMA,
    /// not about the shape of the string: `ID`, `String` and `Date` are all text on the wire and none of
    /// them is substitutable for another in a declaration. Tag against the schema, never against the value
    /// (#848).
    type Var =
        | VString of string
        /// A GraphQL `ID!`. It is a string on the wire, but it is NOT free text — keeping it apart from
        /// `VString` is what stops a field id being passed where an option id belongs.
        ///
        /// It is NOT for anything that merely LOOKS like an id: `singleSelectOptionId` and `iterationId`
        /// are both typed `String` by the schema, and tagging them `VId` declared `ID!` against a `String`
        /// argument — which refused every single-select write the tool made (#848).
        | VId of string
        /// A GraphQL `Date!` (`YYYY-MM-DD`). Apart from `VString` for the same reason `VId` is: the scalar
        /// is named `Date`, so `String!` against it is refused on sight.
        | VDate of string
        | VNumber of double

    /// What travels in the request body.
    type Payload =
        | NoBody
        /// A REST body, already serialised. `sub_issue_id` must be a JSON NUMBER here, not a string — the
        /// bash client used `gh api -F` for exactly this reason and a `-f` (string) form collects a 422.
        | Json of string
        /// A GraphQL document plus its variables. ONE document is ONE call, whatever it selects.
        | Query of document: string * variables: (string * Var) list

    type Request =
        { Method: string
          /// Relative to the API base — `repos/{o}/{r}/issues/{n}/comments`, or `graphql`.
          Path: string
          Query: (string * string) list
          Body: Payload
          /// Which meter this spends. Stated by the CALLER, never inferred from the path, because `Free`
          /// is a decision about billing and it must be visible where the call is made.
          Budget: Budget
          /// The conditional-request header. `None` means the request is UNCONDITIONAL — and for the
          /// lock, that is mandatory, not an optimisation: a 304 serving a body captured before a claim
          /// was posted would report `comments: 0` and hide a live marker. **A lock may never be read
          /// from a cache**, so it is never sent with an ETag.
          IfNoneMatch: string option
          /// What this request is ABOUT, for diagnostics — `FS-GG/FS.GG.SDD#42`, `the board scan`. It
          /// travels with the request so an error can name its subject rather than its payload.
          Subject: string }

    type Response =
        { Status: int
          Body: string
          ETag: string option
          /// The `Link: rel="next"` URL, when the server paginated. Following it is the adapter's job —
          /// the bash client passed `--paginate` to `gh` and the corpus asserts the scan is paginated,
          /// because *a lock has no 100-issue limit* and a truncated first page is a claim scan that
          /// silently cannot see half the markers.
          NextLink: string option }

    /// A 304 is NOT an error and it is NOT an empty body. It is "what you already have is current", and
    /// the caller must have somewhere to get that from — `Cache.conditional` is the only sanctioned way to
    /// issue one, because it is the only thing that can guarantee a cached body exists to be served.
    val (|NotModified|_|): response: Response -> unit option

    /// The transport.
    ///
    /// SYNCHRONOUS, DELIBERATELY. The client is a sequential CLI invoked once per scheduling call, on
    /// every worker, in a fan-out — startup cost is the whole of its runtime cost. There is no concurrency
    /// to win here and an async seam would buy nothing but a harder fake.
    type IGitHubTransport =
        abstract Send: request: Request -> IoResult<Response>

    /// The real one.
    ///
    /// `apiBase` is CONFIGURABLE (`FSGG_GITHUB_API_BASE`, default `https://api.github.com`). That is
    /// ADR-0040 C1's mechanism for keeping the shell corpus black-box: it can stand the tool up against a
    /// local fixture server and observe exactly what the `gh` stub observed — endpoint, method, headers,
    /// body — without the tool knowing it is under test.
    ///
    /// It follows `Link: rel=next` when asked, and it never retries a rate limit: an exhausted budget is
    /// not a lost race, and three more attempts just spend REST calls confirming the same 403.
    type HttpTransport =
        new: apiBase: string * token: string -> HttpTransport

        interface IGitHubTransport
        interface System.IDisposable

    /// Read the API base from the environment, so the corpus can redirect it.
    val apiBaseFromEnv: unit -> string
