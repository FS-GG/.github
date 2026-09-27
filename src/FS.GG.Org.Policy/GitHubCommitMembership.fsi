namespace FS.GG.Org.Policy

/// Validate repository-scoped GraphQL commit membership provisionally. This contract supplies
/// no credential, transport, accepted pin source, or policy authority.
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
    /// The reader still needs authenticated GitHub transport and a complete HTTP 200 response;
    /// fixture or caller-supplied bytes alone cannot establish membership.
    val inspectProvisionalMembership:
        pin: GitCommitProvenance.ExactCommitPin -> expectedTreeId: string ->
        reader: IReadOnlyGraphQlReader -> Result<ProvisionalMembership, SyntaxDiagnostic>
