namespace FS.GG.Org.Policy

/// Pure Git tree and blob checks over supplied bytes or an exact read-only object port.
module GitTreeProjects =
    type ObjectKind = Tree | Blob

    type ObjectObservation =
        { ObjectId: string
          Kind: ObjectKind
          Bytes: byte array }

    type IReadOnlyObjectReader =
        abstract ReadExact: ObjectKind * string -> Result<ObjectObservation, unit>

    /// Enumerate project blob IDs from a complete supplied SHA-1 tree closure.
    val inspectSha1:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        Result<(string * string) list, SyntaxDiagnostic>

    /// Check supplied project bytes against tree blob IDs and return SHA-256 digests.
    val bindSha1ProjectBlobDigests:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<(string * string) list, SyntaxDiagnostic>

    /// Read exact reachable trees and project blobs, checking object type, ID, and bytes.
    val materializeReadOnlySha1Snapshot:
        rootTreeId: string -> reader: IReadOnlyObjectReader ->
        Result<(string * byte array) list * (string * byte array) list, SyntaxDiagnostic>
