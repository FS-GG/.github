namespace FS.GG.Coord

/// The operation key — slice 1 of the GitHub-native executor fencing design
/// (`docs/reports/2026-08-04-github-native-executor-fencing-design.md` §3, §11.2), filed as
/// `.github#2311` under `.github#1858`.
///
/// THE ONE DISTINCTION THIS MODULE EXISTS TO KEEP. `#1858` asks for a GitHub-hosted operation
/// identity, and the design answers it with TWO questions that have TWO different answers:
///
///   * **Mutual exclusion** — "may I act on this receiver right now?" — is answered by the *subject*:
///     one operation-lock issue per receiver, whose `fsgg:claim` winner is the holder (§4.1). It needs
///     no key in the marker at all, and NOTHING IN THIS MODULE ANSWERS IT.
///   * **Idempotence** — "has this exact effect already been applied?" — is answered by the *opkey*
///     below, recorded in the effect receipt and checked by the receiver, so that "a repeat of the same
///     `(item, gen, receiver, op)` finds a receipt and collapses" (§4.3).
///
/// Every exported item below names which of the two it answers, because the cost of confusing them is
/// asymmetric: using the subject where the key belongs merely duplicates a no-op, while using the key
/// where the subject belongs would be an exclusion decision made by a value that decides no lock.
///
/// WHAT THIS MODULE IS NOT. The opkey is never written into a CAS marker: §4.1 is emphatic that the
/// write path "gains no code, no prefix, no field, no parameter", and that `pathRepo=` is deliberately
/// NOT reused to smuggle it, because "overloading a parsed field with a second meaning is the drift
/// class this design refuses everywhere else". This module is also PURE — no IO, no transport, no
/// board contact — which is why it is ordered first and why nothing here fences anything on its own.
/// The fencing is slices 2-6; this is the vocabulary they share.
module Operation =

    /// The closed operation vocabulary. Answers NEITHER question by itself: it is the `op` COMPONENT of
    /// the idempotence key, and it is a sum rather than a string on purpose.
    ///
    /// An OPEN vocabulary is how a fourth operation gets added at one call site and silently fails to
    /// dedupe at another, so closedness here is the COMPILER's and not a validator's: there is no
    /// catch-all case, and — deliberately — no `parse: string -> Op`. The only direction across the
    /// string boundary is outward, through `wire`. Adding a case therefore breaks the build at every
    /// consumer that does not handle it, which is the property `.github#2311` acceptance criterion 2
    /// asks for.
    type Op =
        /// `op=merge` — the merge election's scope (§4.2). Carries no payload: the receiver and the
        /// item are already components of the key, so there is nothing left for `merge` to qualify.
        | Merge
        /// `op=dispatch:<event-type>` — the dispatch grant's scope (§4.1).
        | Dispatch of eventType: string
        /// `op=publish:<package>` — a publication effect, keyed per package.
        | Publish of package: string

    /// Which of the key's four components a refusal is about. Exists so a caller can report WHICH
    /// input it got wrong without re-deriving it from the input it already had — the same reason
    /// `Delivery`'s omission reasons are per-case rather than one string.
    type Part =
        /// `item`, spelled `owner/repo#N`.
        | Item
        /// `gen`, the comment id of the winning `fsgg:claim` marker.
        | Generation
        /// `receiver`, the `owner/repo` the effect lands in.
        | Receiver
        /// The string payload of `Dispatch`/`Publish`. `Merge` has no payload and can never produce it.
        | OperationPayload

    /// Why a key could not be composed. A REFUSAL, never a fallback: this core's rule is that a failed
    /// or malformed read must not be able to masquerade as a legitimate answer (`#266`), so there is no
    /// "compose it anyway" arm and no exception path. Answers neither question; it is what you get
    /// INSTEAD of an idempotence key.
    type Refusal =
        /// The component is empty or whitespace.
        | Blank of part: Part
        /// The component carries a control character. This is the refusal that keeps the pre-image
        /// separator OUT OF THE DOMAIN — one of the two clauses that make `compose` injective by
        /// construction rather than by sampling. See `preimage`.
        | ControlCharacter of part: Part
        /// The component is not well-formed UTF-16 — it carries a high surrogate with no low surrogate
        /// after it, or a low surrogate with no high surrogate before it. This is the SECOND clause,
        /// and it is about the ENCODER rather than the separator: `Encoding.UTF8.GetBytes` uses the
        /// replacement fallback, so every unpaired surrogate becomes the same three bytes and two
        /// genuinely different pre-image strings could otherwise compose ONE key. Well-formed astral
        /// characters are ADMITTED — the refusal is exactly as wide as the guarantee needs and no wider.
        | UnpairedSurrogate of part: Part
        /// `item` is not `owner/repo#N`. The board's `<repo>#N` shorthand lands here: it is not GitHub
        /// grammar, and `.github#2107` is what it costs when it reaches a field GitHub parses.
        | ItemNotFullyQualified of item: string
        /// `receiver` is not `owner/repo`.
        | ReceiverNotFullyQualified of receiver: string
        /// `generation` is not a server-assigned comment id in canonical spelling. The engine's
        /// `released` sentinel — what `ClaimGeneration` holds when NO marker is held — lands here by
        /// rule rather than by special case, because a key composed on it would name a tenancy that
        /// does not exist. So does a leading-zero spelling — one generation must not be keyable two
        /// ways — and `0`, which is neither a comment id nor an issue number.
        | GenerationNotServerAssigned of generation: string

    /// The operation key: `sha256(item \n gen \n receiver \n op)`, lowercase hex, per §3.3.
    ///
    /// ANSWERS IDEMPOTENCE, AND ONLY IDEMPOTENCE. It is not a lock, it grants nothing, and holding one
    /// authorizes nothing — §4.3 is explicit that the receipt carrying it "is audit for the merge path
    /// and authority for nothing".
    ///
    /// Representation-transparent, like every other type in this core, but `compose` is the ONLY
    /// sanctioned producer: a value built by hand has not been through the validation that makes the
    /// composition injective, so it is a string wearing a key's name.
    type OpKey =
        | OpKey of string

        /// The lowercase 64-character hex digest.
        member Value: string

    /// The wire spelling of an operation, exactly as §3's component table gives it: `merge`,
    /// `dispatch:<event-type>`, `publish:<package>`. Total and injective over `Op` for
    /// separator-free payloads — the three spellings cannot collide, because `merge` carries no
    /// colon and the other two carry different prefixes.
    ///
    /// Written with no wildcard arm on purpose, so that FS0025 is an ERROR and a fourth case cannot be
    /// added without visiting every consumer. The two projects reach that the same way by two slightly
    /// different routes, and the difference is stated because the earlier wording got it wrong: THIS
    /// project sets `TreatWarningsAsErrors` and then an EMPTY `WarningsNotAsErrors`, which demotes
    /// nothing; `tests/FS.GG.Coord.Core.Tests` sets `TreatWarningsAsErrors` and NO demotion list at
    /// all. Either way nothing demotes FS0025, and it was measured at both consumers rather than read
    /// off the project files.
    val wire: op: Op -> string

    /// The exact bytes that get hashed, or every reason they could not be produced.
    ///
    /// Exported because it is where this module's distinctness claim is DECIDABLE. The claim is about
    /// `compose`, which is `sha256(UTF8(preimage …))`, so it needs TWO clauses and not one — stating
    /// only the first is the error an independent critic caught on this module's first head:
    ///
    ///   1. `String.concat "\n"` is injective over 4-tuples exactly when no field can contain the
    ///      separator, so the domain excludes it: every component is refused for any control
    ///      character, `\n` and `\r` included.
    ///   2. `Encoding.UTF8.GetBytes` is injective exactly on well-formed UTF-16, because its
    ///      REPLACEMENT fallback maps every unpaired surrogate to the same `EF BF BD`. So the domain
    ///      excludes those too. Without this clause `"FS-GG/r\uD800#2311"` and `"FS-GG/r\uDC00#2311"`
    ///      were two admitted, genuinely distinct pre-images composing one key.
    ///
    /// Both refusals happen BEFORE anything is hashed, so "different inputs give different keys" is a
    /// consequence of the construction rather than a property sampled by a handful of examples.
    ///
    /// Answers idempotence (it is the key's pre-image). Refusals accumulate: all of them are reported,
    /// not the first.
    val preimage: item: string -> generation: string -> receiver: string -> op: Op -> Result<string, Refusal list>

    /// The operation key for `(item, gen, receiver, op)`, or every reason there is not one.
    ///
    /// TOTAL and DETERMINISTIC: equal inputs give equal keys, any one component differing gives a
    /// different key, and no input throws. Answers idempotence. `item` is spelled `owner/repo#N` and
    /// `receiver` `owner/repo`; `generation` is the comment id of the winning `fsgg:claim` marker,
    /// which is server-assigned, monotone, and ordered identically for every racer (§3.1) — this
    /// module takes it as given and only checks that it IS one.
    ///
    /// Nothing is normalized: the caller's exact component bytes are keyed, so a caller cannot get a
    /// silently different key from what it believes it asked for.
    val compose: item: string -> generation: string -> receiver: string -> op: Op -> Result<OpKey, Refusal list>

    /// One human-readable line per refusal, for a caller that must report why it did not act. Carries
    /// no judgement and decides nothing.
    val describe: refusal: Refusal -> string
