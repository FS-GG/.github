namespace FS.GG.Coord.Cli

/// Strict local codec and validation projection for an agent-authored delivery-route receipt — the
/// local half of `route validate`.
///
/// IT DELIBERATELY HAS NO FALLBACK ROUTE. A caller must supply a COMPLETE versioned receipt; there
/// is no default, no inferred route, and no partial acceptance. The live `Client` boundary later
/// binds this same decoded shape to an issue comment before an item may be claimed or projected
/// Ready, so a receipt that decoded loosely here would become a durable board fact that nothing had
/// really checked.
///
/// The route decision itself is an agent judgement and is never computed here: this module only
/// refuses to accept one that is not fully stated.
module DeliveryRouteApplication =

    /// Decode one structured route record from JSON text — the ONLY route form accepted by write
    /// paths from M4 onward.
    ///
    /// ALL SEVENTEEN FIELDS MUST BE PRESENT, and their types are checked rather than coerced: seven
    /// required strings (`schema`, `subject`, `policyVersion`, `agent`, `timestamp`, `rationale`,
    /// `digest`), `revision` as a 32-bit integer, five string arrays (`scope`, `dependencies`,
    /// `touchSet`, `reasonCodes`, `requiredGates`), `route` itself, and three nullable strings.
    /// Only `previousDigest`, `sddWorkId` and `specHome` may be `null`, and only those three; a
    /// `null` anywhere else is an error rather than an absent value.
    ///
    /// `route` IS A CLOSED VOCABULARY of exactly `lightweight`, `sdd-required`, or `null` — and
    /// `null` is a distinct, meaningful answer (no route selected yet), not a decode failure. Any
    /// other string is refused by name.
    ///
    /// REPORTS EVERY FAILING FIELD AT ONCE, joined with `"; "`, rather than stopping at the first.
    /// An agent authoring a receipt learns everything wrong with it in one round trip.
    ///
    /// Malformed JSON is distinguished from field-level findings by its own message. This function
    /// performs no IO and validates no ledger semantics — it decodes, and nothing else.
    val decodeStructured: string -> Result<FS.GG.Coord.StructuredDecision.RouteRecord, string>

    /// Run `route validate <subject> <record.json>`, printing one
    /// `fsgg.coord.delivery-route-result/v2` object on stdout.
    ///
    /// Decodes the file, then applies `StructuredDecision.validateRouteLedger` for the named
    /// subject. A clean result is `kind: "current"` with `ExitCode.Green`; anything else is
    /// `kind: "refusal"` carrying every error, with `ExitCode.Error`.
    ///
    /// `record` AND `show` REFUSE HERE BY DESIGN, with a message saying they require the live GitHub
    /// receipt boundary. They are recognised rather than unknown precisely so the refusal can say
    /// WHY — a caller reaching for them locally has the right verb and the wrong boundary, and an
    /// "unknown action" error would send them looking for a typo instead.
    ///
    /// Every refusal — bad usage, unreadable record, failed decode, failed ledger validation — is
    /// reported through the same result object, so a caller parses one shape on every path. The
    /// `errors` array and the joined `detail` string carry the same content in both forms.
    val run: Options.Options -> int
