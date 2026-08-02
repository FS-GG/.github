namespace FS.GG.Coord

/// Deterministic inventory and validation of rename-shaped text changes.  Git and file reads stay at
/// the application edge; this module is deliberately pure so the receipt contract can be tested.
module SemanticDiff =
    type Classification = StringLiteral | CharacterLiteral | Comment | SerializedKey | GoldenText | TestText | Documentation | GeneratedArtifact
    type Disposition = IntendedContractChange | IntendedTestOrDocumentationUpdate | GeneratedOutput | AccidentalFixRequired | Unresolved
    type Occurrence =
        { Id: string; Path: string; Line: int; Classification: Classification; Confidence: int
          Before: string; After: string; Disposition: Disposition }
    type Receipt =
        { SchemaVersion: int; Repository: string; BaseSha: string; HeadSha: string; DeclaredPaths: string list
          Required: bool; Occurrences: Occurrence list }

    val inventory: path: string -> before: string -> after: string -> oldToken: string -> newToken: string -> Occurrence list
    val receipt: repository: string -> baseSha: string -> headSha: string -> declaredPaths: string list -> required: bool -> occurrences: Occurrence list -> Receipt
    val validate: expectedBase: string -> expectedHead: string -> Receipt -> string list
    val toJson: Receipt -> string
    val ofJson: string -> Result<Receipt, string list>
    val toBase64: Receipt -> string
    val ofBase64: string -> Result<Receipt, string list>
    val classificationName: Classification -> string
    val dispositionName: Disposition -> string
