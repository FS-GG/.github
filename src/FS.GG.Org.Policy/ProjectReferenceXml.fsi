namespace FS.GG.Org.Policy

/// Pure project XML readers for Rule B. Inputs are caller-supplied; project discovery,
/// source authentication, and installed receiver wiring remain separate obligations.
module ProjectReferenceXml =
    /// Resolve direct static project references from one supplied project document.
    /// The reader accepts normalized repository-relative identities and well-formed XML only.
    /// It refuses project constructs whose evaluated reference set cannot be derived statically.
    /// Explicit imports, target-time references, task outputs, Remove items, and unverified SDKs
    /// require MSBuild evaluation. This reader makes no complete-roster or filesystem claim.
    val inspect: projectPath: string -> xml: string -> Result<string list, SyntaxDiagnostic>

    /// Assemble a closed graph from a nonempty supplied set of project identities and XML.
    /// Duplicate identities, unsupported extensions, and references without supplied nodes refuse.
    /// This does not authenticate discovery, file bytes, implicit imports, or evaluated items.
    val inspectSuppliedProjectSet:
        sources: (string * string) list -> Result<Map<string, string list>, SyntaxDiagnostic>

    /// Require the supplied sources to match a separately supplied project roster exactly.
    /// The roster remains caller-authenticated; this operation checks only exact local agreement.
    val inspectSuppliedProjectSetAgainstRoster:
        expected: string list ->
        sources: (string * string) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Bind each supplied byte stream to its expected SHA-256 digest before graph reduction.
    /// Digests must be canonical lowercase hexadecimal values and every expected source must exist.
    /// XML declarations govern byte decoding, so an intermediary text conversion cannot certify it.
    /// Digest provenance and complete provider enumeration remain external requirements.
    val inspectSuppliedProjectBytesAgainstDigests:
        expected: (string * string) list ->
        sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Bind a supplied raw Git tree and exact project blobs to a provisional graph.
    /// Bytes are copied once so ID checks and XML parsing observe the same local snapshot.
    /// The root still needs authenticated repository and commit provenance.
    val inspectSuppliedGitSnapshot:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<Map<string, string list>, SyntaxDiagnostic>

    /// Fetch exact Git objects from a read-only port before building a provisional graph.
    /// The root still needs authenticated commit provenance.
    val inspectReadOnlyGitObjectSnapshot:
        rootTreeId: string -> reader: GitTreeProjects.IReadOnlyObjectReader ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Require a provisional exact commit/root match before reducing supplied Git objects.
    /// Reader custody and the source of the accepted pin remain external trust boundaries.
    val inspectSuppliedPinnedGitSnapshot:
        pin: GitCommitProvenance.ExactCommitPin -> reader: GitCommitProvenance.IReadOnlyCommitReader ->
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<Map<string, string list>, SyntaxDiagnostic>

    /// Require provisional GitHub commit membership before reducing supplied Git objects.
    /// The GraphQL reader and accepted pin remain uninstalled trust boundaries.
    val inspectSuppliedGitHubMembershipSnapshot:
        pin: GitCommitProvenance.ExactCommitPin ->
        membershipReader: GitHubCommitMembership.IReadOnlyGraphQlReader ->
        commitReader: GitCommitProvenance.IReadOnlyCommitReader -> rootTreeId: string ->
        treeObjects: (string * byte array) list -> sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Observe the protected tip before and after a supplied Git snapshot, checking repository
    /// membership, commit bytes, tree bytes, and project bytes between those observations.
    val inspectSuppliedProtectedBranchSnapshot:
        repository: GitHubProtectedBranchPin.ExactRepository ->
        branchReader: GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader ->
        membershipReader: GitHubCommitMembership.IReadOnlyGraphQlReader ->
        commitReader: GitCommitProvenance.IReadOnlyCommitReader -> rootTreeId: string ->
        treeObjects: (string * byte array) list -> sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Observe the protected tip before and after exact read-only object reads.
    /// Provider authentication, source acceptance, and evaluated MSBuild remain external.
    val inspectReadOnlyProtectedBranchSnapshot:
        repository: GitHubProtectedBranchPin.ExactRepository ->
        branchReader: GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader ->
        membershipReader: GitHubCommitMembership.IReadOnlyGraphQlReader ->
        commitReader: GitCommitProvenance.IReadOnlyCommitReader -> rootTreeId: string ->
        objectReader: GitTreeProjects.IReadOnlyObjectReader ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// A bounded observation that one supplied implicit file declares no direct reference.
    /// This observation does not prove nearest-file selection, import closure, or source provenance.
    type SuppliedImplicitObservation = NoDirectReferenceInSuppliedXml

    /// Inspect one supplied Directory.Build.props or Directory.Build.targets document locally.
    /// References, unresolved imports, selection overrides, and dynamic task outputs all refuse.
    val inspectSuppliedImplicitXml:
        sourcePath: string ->
        xml: string ->
        Result<SuppliedImplicitObservation, SyntaxDiagnostic>
