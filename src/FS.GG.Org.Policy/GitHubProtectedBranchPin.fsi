namespace FS.GG.Org.Policy

/// Provisional protected-main tip observation; provider and pin acceptance remain external.
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
    val inspectProvisionalPin:
        repository: ExactRepository -> reader: IReadOnlyProtectedBranchReader ->
        Result<GitCommitProvenance.ExactCommitPin, SyntaxDiagnostic>
