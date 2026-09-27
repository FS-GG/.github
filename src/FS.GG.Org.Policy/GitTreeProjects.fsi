namespace FS.GG.Org.Policy

/// Pure SHA-1 Git tree and blob checks over supplied bytes or an exact-object reader.
/// Authenticating the root to a repository commit remains a separate provider obligation.
module GitTreeProjects =
    type ObjectKind = Tree | Blob

    type ObjectObservation =
        { ObjectId: string
          Kind: ObjectKind
          Bytes: byte array }

    type IReadOnlyObjectReader =
        abstract ReadExact: ObjectKind * string -> Result<ObjectObservation, unit>

    /// Enumerate project blob IDs from a complete supplied SHA-1 tree closure.
    /// IDs do not prove that separately supplied project bytes match the blobs, and a
    /// caller-provided root ID is not an authenticated commit anchor.
    val inspectSha1:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        Result<(string * string) list, SyntaxDiagnostic>

    /// Check an exact project-byte set against tree blob IDs and return SHA-256 digests.
    /// This does not authenticate the root tree to a repository or commit.
    val bindSha1ProjectBlobDigests:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<(string * string) list, SyntaxDiagnostic>

    /// Read exact reachable trees and project blobs, checking object type, ID, and bytes.
    /// This port has no installed local or network reader; root commit provenance is separate.
    val materializeReadOnlySha1Snapshot:
        rootTreeId: string -> reader: IReadOnlyObjectReader ->
        Result<(string * byte array) list * (string * byte array) list, SyntaxDiagnostic>
