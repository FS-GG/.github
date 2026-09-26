namespace FS.GG.Org.Policy

/// Pure project XML readers. Callers retain responsibility for roster and source provenance.
module ProjectReferenceXml =
    /// Resolve direct static project references from one supplied project document.
    /// The reader accepts normalized repository-relative identities and well-formed XML only.
    /// It refuses project constructs whose evaluated reference set cannot be derived statically.
    val inspect: projectPath: string -> xml: string -> Result<string list, SyntaxDiagnostic>

    /// Assemble a closed graph from a nonempty supplied set of project identities and XML.
    /// Duplicate identities, unsupported extensions, and references without supplied nodes refuse.
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
    val inspectSuppliedProjectBytesAgainstDigests:
        expected: (string * string) list ->
        sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Bind a supplied raw Git tree and exact project blobs to a provisional graph.
    val inspectSuppliedGitSnapshot:
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<Map<string, string list>, SyntaxDiagnostic>

    /// Fetch exact Git objects from a read-only port before building a provisional graph.
    val inspectReadOnlyGitObjectSnapshot:
        rootTreeId: string -> reader: GitTreeProjects.IReadOnlyObjectReader ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Require a provisional exact commit/root match before reducing supplied Git objects.
    val inspectSuppliedPinnedGitSnapshot:
        pin: GitCommitProvenance.ExactCommitPin -> reader: GitCommitProvenance.IReadOnlyCommitReader ->
        rootTreeId: string -> treeObjects: (string * byte array) list ->
        sources: (string * byte array) list -> Result<Map<string, string list>, SyntaxDiagnostic>

    /// Require provisional GitHub commit membership before reducing supplied Git objects.
    val inspectSuppliedGitHubMembershipSnapshot:
        pin: GitCommitProvenance.ExactCommitPin ->
        membershipReader: GitHubCommitMembership.IReadOnlyGraphQlReader ->
        commitReader: GitCommitProvenance.IReadOnlyCommitReader -> rootTreeId: string ->
        treeObjects: (string * byte array) list -> sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Observe the protected tip before and after a supplied Git snapshot.
    val inspectSuppliedProtectedBranchSnapshot:
        repository: GitHubProtectedBranchPin.ExactRepository ->
        branchReader: GitHubProtectedBranchPin.IReadOnlyProtectedBranchReader ->
        membershipReader: GitHubCommitMembership.IReadOnlyGraphQlReader ->
        commitReader: GitCommitProvenance.IReadOnlyCommitReader -> rootTreeId: string ->
        treeObjects: (string * byte array) list -> sources: (string * byte array) list ->
        Result<Map<string, string list>, SyntaxDiagnostic>

    /// Observe the protected tip before and after exact read-only object reads.
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
