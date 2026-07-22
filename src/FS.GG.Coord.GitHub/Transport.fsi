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
          /// The response's validator, for a conditional re-read.
          ///
          /// **`None` WHENEVER `Body` IS A MERGE, AND THAT IS A GUARANTEE THIS TYPE MAKES.** An ETag belongs
          /// to the request that returned it — page ONE — and `Send` merges the pages below it. A validator
          /// that outlived its page would revalidate a whole collection against its first page: a set that
          /// grows a page while page one stays byte-identical answers 304, the merge never runs, and the
          /// caller is handed a one-page body for a two-page set. That is #461 — a partial read wearing a
          /// complete one's clothes — and downstream it decides whether to merge.
          ///
          /// It is dropped HERE, at the only layer that knows a merge happened, rather than guarded at each
          /// caller. A caller cannot see how many requests its read cost, so a rule asking it to reason about
          /// that is a rule it will get wrong once and silently. Compare `Cache.defer`, which takes the
          /// `IoError` that licenses a deferral so a caller CANNOT queue a write without holding one: the
          /// type is what stops it being rewritten.
          ///
          /// A single-page response still carries its ETag. Whether that page may be MEMOISED is a further
          /// question this cannot answer — see `Reads.memoisable`, which also demands headroom.
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

    /// Which concrete `ProjectV2Owner` a coordination board hangs off.
    ///
    /// An org-owned board and a user-owned board answer to DIFFERENT GraphQL root fields —
    /// `organization(login:)` and `user(login:)` — so the owner kind has to be chosen before the Projects
    /// v2 query is built. A user login queried through `organization(login:)` resolves to `null` and every
    /// board read/write fails. `Org` is the default, and its documents are byte-identical to the ones that
    /// preceded this type — the FS-GG board is untouched. `Viewer` (#1349) resolves the board from the
    /// token's own `viewer` identity, needing no login in config at all.
    type OwnerKind =
        | Org
        | User
        | Viewer

    module OwnerKind =

        /// The GraphQL root field that resolves this owner, and the `data.<field>` the response is nested
        /// under. `Org`/`User` resolve by login; `Viewer` is the authenticated token's own User.
        val ownerField: kind: OwnerKind -> string

        /// Rewrite an ORG-shaped ProjectV2 query for this owner kind. `Org` is a no-op (byte-identical);
        /// `User` swaps `organization(login: $owner)` for `user(login: $owner)`; `Viewer` swaps it for the
        /// argument-less `viewer` root and drops the now-unused `$owner: String!` variable declaration.
        val forOwner: kind: OwnerKind -> orgQuery: string -> string

        /// The owner variables this kind binds. `Org`/`User` bind `$owner`; `Viewer` binds nothing — its
        /// document declares no `$owner`, and GitHub rejects a request supplying an undeclared variable.
        val ownerVars: kind: OwnerKind -> owner: string -> (string * Var) list

        /// The owner kind for this client, from `FSGG_COORD_OWNER_TYPE` (and, for `user`, whether an explicit
        /// `FSGG_COORD_OWNER` is set). `user` with a login is `User`; `user` with no login is `Viewer`;
        /// anything else (unset, empty, `org`, `organization`, or unrecognised) is `Org`.
        val fromEnv: unit -> OwnerKind
