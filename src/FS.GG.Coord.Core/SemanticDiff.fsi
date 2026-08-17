namespace FS.GG.Coord

/// Deterministic inventory and validation of rename-shaped text changes.  Git and file reads stay at
/// the application edge; this module is deliberately pure so the receipt contract can be tested.
module SemanticDiff =
    type Classification =
        | StringLiteral
        | CharacterLiteral
        | Comment
        | SerializedKey
        | GoldenText
        | TestText
        | Documentation
        | GeneratedArtifact

    type Disposition =
        | IntendedContractChange
        | IntendedTestOrDocumentationUpdate
        | GeneratedOutput
        | AccidentalFixRequired
        | Unresolved

    type Occurrence =
        { Id: string
          Path: string
          Line: int
          Classification: Classification
          Confidence: int
          Before: string
          After: string
          Disposition: Disposition }

    type Receipt =
        { SchemaVersion: int
          Repository: string
          BaseSha: string
          HeadSha: string
          OldToken: string
          NewToken: string
          DeclaredPaths: string list
          Required: bool
          Occurrences: Occurrence list }

    /// What the ENGINE independently established about the diff, for a chain that submits receipts.
    /// `Expected` proves each submitted receipt honest about the pair it names; `Discovered` proves the
    /// submitted receipts complete against the population the engine found for itself. A receipt carries
    /// one rename pair, so honesty alone let a receipt for 6 of 12 discovered occurrences validate
    /// (.github#2144 repair-phase round 2).
    type TrustedAudit =
        { /// One engine recomputation per submitted receipt, matched by rename pair and declared paths.
          Expected: Receipt list
          /// Every occurrence the engine discovered across the whole diff, receipts notwithstanding.
          Discovered: Occurrence list }

    /// Every semantic occurrence of the rename in one file's base/head pair.
    ///
    /// Rename-shaped lines are aligned by their token-substituted CONTENT rather than by line number, so
    /// insertions and deletions elsewhere in the file cannot hide an occurrence. Repeated equal lines are
    /// paired first-to-first, which is what keeps the inventory deterministic.
    val inventory:
        path: string -> before: string -> after: string -> oldToken: string -> newToken: string -> Occurrence list

    /// Recovers the rename tokens from a live base/head diff, for the delivery path where no receipt
    /// supplies them.  Each element of `files` is `path, contentAtBase, contentAtHead`.  Deduplicated
    /// and ordered, so one diff always yields one answer.
    val discoverRenames: files: (string * string * string) list -> (string * string) list

    /// Every occurrence the discovered renames account for.  This is the occurrence count the threshold
    /// is measured against when no receipt was submitted — never the changed-FILE count, which is a
    /// different quantity and always a lower bound (.github#2144).
    val discoveredOccurrences: files: (string * string * string) list -> Occurrence list

    val activationRequired:
        threshold: int -> occurrenceCount: int -> commitMessage: string -> itemBody: string option -> bool

    val receipt:
        repository: string ->
        baseSha: string ->
        headSha: string ->
        oldToken: string ->
        newToken: string ->
        declaredPaths: string list ->
        required: bool ->
        occurrences: Occurrence list ->
            Receipt

    val validate: expectedBase: string -> expectedHead: string -> Receipt -> string list
    val validateAgainst: expected: Receipt -> submitted: Receipt -> string list
    val toJson: Receipt -> string
    val ofJson: string -> Result<Receipt, string list>
    val toBase64: Receipt -> string
    val ofBase64: string -> Result<Receipt, string list>
    val classificationName: Classification -> string
    val dispositionName: Disposition -> string
