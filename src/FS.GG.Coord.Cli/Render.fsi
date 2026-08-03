namespace FS.GG.Coord.Cli

/// THE WIRE-JSON RENDERERS — the machine contracts (`ready`, `who`, `claim`/`take`, `inbox`, `lint`),
/// extracted out of `Client` as ADR-0047's second Client.fs decomposition seam. Each hand-writes its
/// array with a real `Utf8JsonWriter`, so a worker id, path, branch, or title carrying a quote cannot
/// forge the shape a consumer parses. The `who`/`lint` presentation DTOs travel WITH their renderers —
/// they are the renderers' input shape and nothing else's — so the wire contract and the type it
/// serialises live behind one `.fsi`. `Client`'s handlers gather the data; these render it.
module Render =

    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.GitHub

    /// A `who` row's lock state. "In flight" is a LOCK fact, not a column fact: an item is in flight when
    /// a marker holds it — LIVE (`Held`) or past its lease (`Stale`, still a lock only `reap` may break) —
    /// or when the board column says In progress with NO marker (`Unclaimed`). The JSON `.state` a
    /// consumer keys on (held/stale/unclaimed) is derived from this.
    ///
    /// `Undetermined` (.github#1668) is the FOURTH answer, and it is not a lock state: it is "the marker read
    /// returned comments I could not classify, so I cannot tell you whether this item is held". It must never
    /// collapse into `Unclaimed` — that is the fail-open direction, and it carries an accusation.
    type WhoState =
        | Held of Reads.Marker
        | Stale of Reads.Marker
        | Unclaimed
        /// The marker read was incomplete AND yielded no marker at all, so there is no lock state to
        /// report. The reasons live on `WhoRow.Incomplete` — an incomplete read is a property of the READ,
        /// not of this verdict, and a `Held` or `Stale` row can suffer it too.
        | Undetermined

    /// A classified in-flight row: the item, its lock state, the paths it reserves, and — on a STALE row
    /// only — the proof-of-life a human needs before reaping (#581/#697/#1055).
    type WhoRow =
        { Ref: Ref
          State: WhoState
          Paths: string list
          /// The item's own OPEN `item/<n>-*` PR as (number, headRef), when the lease lapsed but the WORK
          /// did not (#581). `Some` only on a Stale row.
          LivePr: (int * string) option
          /// #1055: the lease lapsed, there is NO open PR, but a pushed `item/<n>-*` branch exists — proof
          /// of life during §3. `true` only on a Stale row. Mutually exclusive with `LivePr`.
          BranchPushed: bool
          /// What that PR says (#697), when there is one — is the finished work landable? `Some` exactly
          /// when `LivePr` is `Some`.
          PrState: PrState option
          /// `who --local` — the local git worktree this item is checked out in, if any (#959). Emitted
          /// only when `--local` was asked.
          Worktree: string option

          /// EVERY COMMENT THE MARKER READ COULD NOT CLASSIFY (.github#1668) — on EVERY row, whatever its
          /// state, because an incomplete read is a property of the READ and not of the verdict drawn from
          /// it. Empty on the overwhelmingly normal row, and empty is the load-bearing value: only an empty
          /// list licenses acting on this row's state as a fact.
          ///
          /// It is NOT redundant with `Undetermined`. That state is the case where the short read left NO
          /// marker at all; this field also fires on `Held` and `Stale`, where a marker WAS found and the
          /// hidden one may be a lower id (so the named holder is the wrong holder) or a live claim behind
          /// a lapsed one (so the `STALE` a human is about to `reap` is not free).
          Incomplete: string list }

    /// The fresh postcondition emitted by `claim --json` and `take --json`. The lock and board column are
    /// separate observations; `Converged` is true only when both were read back successfully.
    type ClaimReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          MarkerObserved: bool
          MarkerId: int64 option
          AssigneeObserved: string option
          Status: string option
          StatusRead: string
          StatusWrite: string
          PendingBoardWrites: int option
          Converged: bool }

    /// The OTHER outcome of `take --json`: it looked, and it claimed nothing (.github#1525). A LOOK THAT
    /// SUCCEEDED — `take`'s EX_NONE — so it is a receipt of its own rather than the absence of one.
    ///
    /// It is a separate type rather than a `ClaimReceipt` with everything optional, because the two
    /// documents share only the question they answer. Every field of `ClaimReceipt` past `kind` is a
    /// POSTCONDITION OF A MUTATION — a marker read back, a column written — and there was no mutation
    /// here. Modelling them as `None` would put `"markerObserved":false` and `"converged":false` on the
    /// wire for a command that wrote nothing and therefore observed nothing, which reads as a claim that
    /// FAILED. `kind` tells the two apart, and it is the only key a consumer must branch on.
    type NoItemReceipt =
        { Worker: string
          /// How many candidates the scheduler LOOKED AT and refused. #428's distinction in machine form:
          /// `0` is a genuinely empty queue, and anything higher is a BUSY one whose items are behind
          /// claims, columns or blockers — the same fact, and two opposite instructions to the caller.
          /// The per-item REASONS stay on stderr, where `batch --json` already puts them.
          PassedOver: int
          /// `Scan.Receipt.RepoAdvisory` — "the `--repo` you named matched nothing on this board" (#979).
          ///
          /// IT RIDES IN THE DOCUMENT BECAUSE THIS DOCUMENT IS NEW. #979 put the advisory on stderr
          /// because the only reader was a human: `take` had no machine projection of this outcome to
          /// carry it in. Now it does, and `passedOver:0` is EXACTLY what a typo'd `--repo` produces —
          /// indistinguishable, to a parser, from a board that is genuinely empty. A driver would read
          /// "this repo has no work" off a misspelling and stop dispatching to a full repo, which is the
          /// harm #979 exists to prevent, arriving on the surface this receipt creates. `None` (wire
          /// `null`) whenever the scope named something, which is every healthy call.
          RepoAdvisory: string option }

    /// A single `lint` finding, in the shape `lint --json` emits.
    type LintFinding =
        { Code: string
          Severity: string
          Id: string
          Short: string
          Status: string
          Url: string
          Detail: string }

    /// A `predicate --json` result — the ADR-0050 oracle verdict, structured (.github#1202). `ownerValue`
    /// and `note` are non-null exactly on `contradicts`; `reason` on `unknown`.
    type PredicateResult =
        { Verdict: string
          Id: string
          Field: string
          Value: string
          OwnerValue: string option
          Note: string option
          Reason: string option }

    /// ONE claim a path update now collides with (.github#1517) — the same three facts the human OVERLAP
    /// branch prints (`OVERLAP — now collides with <ref> (worker <holder>)` / `  <tokens>`), plus whether
    /// the courtesy notice this command posts on that holder's item actually landed. The notify outcome is
    /// part of the receipt because a notice that FAILED still leaves a standing collision: the human form
    /// says so on stderr, so the machine form must not have to infer it from silence.
    type PathCollision =
        { Ref: Ref
          Worker: string
          /// The shared token STEMS. A LIST, rendered as an array — the human form joins them into one
          /// stderr line, and a machine field shaped to that line would be a consumer splitting on ", ".
          SharedTokens: string list
          Notified: bool
          /// Why the notice failed; `None` when it landed.
          NotifyError: string option }

    /// The receipt `widen --json` and `set-paths --json` emit (.github#1517): the ref, the declaration the
    /// update RESULTED IN, and the #353 overlap verdict — all three in ONE object, so a machine consumer
    /// never scrapes `widened <ref> → Paths: a, b` prose or reads the overlap detail off a second stream.
    /// `Kind` is the past-tense verb (`widened`/`set`), mirroring `ClaimReceipt.Kind`'s `claimed`.
    /// `Collisions` is empty exactly when the verdict is `disjoint`.
    type PathUpdateReceipt =
        { Ref: Ref
          Worker: string
          Kind: string
          /// The tokens the item now declares — the resulting touch-set, not the tokens that were asked for.
          Paths: string list
          Collisions: PathCollision list }

    /// How ONE mechanical repair went under `reconcile --apply` (.github#1524). The wire words are
    /// `ClaimReceipt.StatusWrite`'s (`written`/`deferred`/`not-on-board`) rather than the human line's
    /// (`applied`/`queued`), because those are the same cases of the same `Board.WriteOutcome` reported by
    /// the same CLI — one fact, one name. `Deferred` is the QUEUED write nothing replays for you, and it is
    /// a case of a closed union rather than a word in a sentence. `Reaped` is deliberately weaker than
    /// `Written`: `STALE-CLAIM` is delegated to the `reap` verb once per repo, so it reports that the pass
    /// covering this item exited green, not a per-item read-back.
    type ReconcileOutcome =
        | Written
        | Deferred
        | NotOnBoard
        | Reaped
        | NotAttempted of reason: string
        | Failed of reason: string

    /// The wire word for an outcome.
    val reconcileOutcomeName: outcome: ReconcileOutcome -> string

    /// One row of `reconcile --json`: a mechanical finding, plus — under `--apply` — the field write it
    /// attempted and how that went.
    type ReconcileRow =
        { Id: string
          Rule: string
          Subject: Ref
          Size: string
          Remedy: string
          Statement: string
          /// The field and value this repair sets; `None` for `STALE-CLAIM`, which writes no field. One
          /// option over the PAIR, so the two cannot be present independently.
          Write: (string * string) option
          Writes: (string * string) list
          Observed: (string * string) list option
          /// `None` on a dry run — nothing was attempted, so nothing is known.
          Outcome: ReconcileOutcome option }

    /// `ready --json` — the machine contract a reconciler reads: a JSON array of the startable rows. A
    /// real JSON writer, so a title or path carrying a quote cannot forge the array.
    val renderReadyJson: rows: Scan.Row list -> string

    /// `who --json` — the machine contract cases 20/25 certify: a JSON array of the in-flight items, each
    /// with `number`, `repo`, `state`, the `worker` (`null` when unclaimed OR undetermined), and the `paths`
    /// it reserves; a STALE row also carries `livePr`/`prState`/`branchPushed`, and ANY row whose marker read
    /// was incomplete carries `undetermined` — the reasons — including `held`/`stale` rows (.github#1668). `includeWorktree` is `who --local`
    /// (#959): the `worktree` field is emitted ONLY when asked, so the no-`--local` shape is byte-identical.
    val renderWhoJson: includeWorktree: bool -> rows: WhoRow list -> string

    /// `claim --json` / `take --json` — one typed mutation receipt, safe to gate worker startup on.
    val renderClaimReceiptJson: receipt: ClaimReceipt -> string

    /// `take --json` when the queue handed out nothing (.github#1525) — the SAME projection's other
    /// outcome, so a caller parses one stream and reads `kind` rather than branching on the exit code to
    /// decide whether stdout is JSON at all.
    ///
    /// `ref`/`repo`/`number` are emitted as EXPLICIT NULLS in `renderClaimReceiptJson`'s own key order.
    /// That is the point of the shape: one key set in both outcomes, with "there is no item" MODELLED
    /// (#437) rather than signalled by a missing key. A consumer that reads `.ref` gets `null`, not a
    /// KeyError, and the document describes its own outcome without the exit code held beside it.
    val renderNoItemJson: receipt: NoItemReceipt -> string

    /// `inbox --json` — a JSON array of the messages addressed to this worker.
    val renderInboxJson: msgs: (string * Reads.Message) list -> string

    /// `lint --json` — a JSON array of the findings, each carrying its code, severity, and the item it is
    /// about. A real JSON writer, so a finding detail carrying a quote cannot forge the array.
    val renderLintJson: findings: LintFinding list -> string

    /// `predicate --json` — a single JSON object: the ADR-0050 verdict and its assertion (.github#1202).
    /// A real JSON writer, so a governing note carrying a quote cannot forge the object.
    val renderPredicateJson: result: PredicateResult -> string

    /// JSON-mode failures, written to stderr so stdout stays a success-only document channel.
    val renderFailureJson: exitCode: int -> message: string -> rateLimit: Errors.RateLimitKind option -> string

    /// `reconcile --json` / `reconcile --apply --json` — the findings array, and under `--apply` how each
    /// repair went (.github#1524), so a mutating verb's outcome is IN its document rather than printed
    /// past it.
    ///
    /// THE FIRST SIX KEYS ARE ALPHABETICAL (`id,remedy,rule,size,statement,subject`) BY CONTRACT, not by
    /// preference: this projection was an F# anonymous record, whose fields the compiler sorts, so those
    /// are the bytes existing consumers already parse. The tests pin them. Do not reorder.
    ///
    /// `includeOutcome` means "these rows carry an attempt's result", decided once per DOCUMENT
    /// (`renderWhoJson`'s `includeWorktree` precedent, #959): the dry-run shape stays byte-identical, and
    /// an apply run cannot emit a ragged array. It appends exactly the facts an attempt adds — `field`,
    /// `value`, `outcome`, `error`.
    ///
    /// It is NOT simply `--apply`: an `--apply` run with no findings passes `false`, because there is no
    /// attempt to report and the array is empty either way.
    val renderReconcileJson: includeOutcome: bool -> rows: ReconcileRow list -> string

    /// `widen --json` / `set-paths --json` — one typed touch-set receipt (.github#1517). `verdict` is
    /// `disjoint` or `overlap`, derived from `Collisions` rather than carried beside it, so the two can
    /// never disagree. A real JSON writer, so a path token or worker id carrying a quote cannot forge the
    /// object.
    val renderPathUpdateJson: receipt: PathUpdateReceipt -> string
