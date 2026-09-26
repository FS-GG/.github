namespace FS.GG.Org.Policy

/// Derive a provisional pin from an exact protected-main observation. An authenticated,
/// no-redirect REST reader and accepted repository identity remain external prerequisites.
module GitHubProtectedBranchPin =
    type ExactRepository =
        { RepositoryNodeId: string
          RepositoryFullName: string }

    type ExactRequest =
        { Url: string
          Owner: string
          Name: string
          Branch: string }

    type BranchResponse =
        { StatusCode: int
          ResponseUrl: string
          MediaType: string
          Body: byte array }

    type IReadOnlyProtectedBranchReader =
        abstract ReadExact: ExactRequest -> Result<BranchResponse, unit>

    /// Admit only the fixed GitHub protected-main URL and exact repository path.
    val internal isExactReadRequest: request: ExactRequest -> bool

    /// Check an exact read-only branch response and return a provisional commit pin.
    /// One observed tip is neither an immutable acceptance receipt nor a guarantee that main
    /// remains at that tip; repository-ID membership requires a later GraphQL check.
    val inspectProvisionalPin:
        repository: ExactRepository -> reader: IReadOnlyProtectedBranchReader ->
        Result<GitCommitProvenance.ExactCommitPin, SyntaxDiagnostic>
