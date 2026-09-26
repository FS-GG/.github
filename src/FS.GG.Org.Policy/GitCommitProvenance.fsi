namespace FS.GG.Org.Policy

/// Bind a supplied raw Git commit to an exact tree provisionally. The reader's repository
/// identity and completeness must be authenticated before this result has policy authority.
module GitCommitProvenance =
    type ExactCommitPin =
        { RepositoryNodeId: string
          RepositoryFullName: string
          CommitId: string }

    type CommitObservation =
        { RepositoryNodeId: string
          RepositoryFullName: string
          CommitId: string
          RawCommit: byte array }

    type IReadOnlyCommitReader =
        abstract ReadExact: ExactCommitPin -> Result<CommitObservation, unit>

    type ProvisionalRoot =
        { RepositoryNodeId: string
          CommitId: string
          TreeId: string }

    /// Accept only an opaque node ID without whitespace or control characters.
    val internal validRepositoryNodeId: value: string -> bool
    /// Accept only one exact repository path segment.
    val internal validRepositorySegment: value: string -> bool
    /// Accept only an exact owner/repository identity.
    val internal validRepositoryFullName: value: string -> bool

    /// Check raw SHA-1 commit bytes and exact pin/reader claims for a provisional root.
    /// A fake reader can fabricate membership; an accepted pin and authenticated provider
    /// remain prerequisites. Branch and ref aliases do not substitute for an exact commit ID.
    val inspectProvisionalRoot:
        pin: ExactCommitPin -> expectedTreeId: string -> reader: IReadOnlyCommitReader ->
        Result<ProvisionalRoot, SyntaxDiagnostic>
