namespace FS.GG.Org.Policy

/// Provisional repository-scoped GraphQL commit membership over an exact read-only port.
module GitHubCommitMembership =
    type ExactRequest =
        { Document: string
          Owner: string
          Name: string
          CommitId: string }

    type IReadOnlyGraphQlReader =
        abstract ExecuteExact: ExactRequest -> Result<byte array, unit>

    type ProvisionalMembership =
        { RepositoryNodeId: string
          CommitId: string
          TreeId: string }

    /// Admit only the fixed document, exact repository, and canonical commit ID.
    val internal isExactReadRequest: request: ExactRequest -> bool

    /// Check repository identity, commit membership, and tree ID in a bounded response.
    val inspectProvisionalMembership:
        pin: GitCommitProvenance.ExactCommitPin -> expectedTreeId: string ->
        reader: IReadOnlyGraphQlReader -> Result<ProvisionalMembership, SyntaxDiagnostic>
