namespace NewSddWorkspace

/// Prepare receiver-local V2 integration with the canonical adapter contract. This module installs
/// local bytes only; native board mutation, publication and installed adoption remain independent.
module ProductBoard =
    open System.IO
    open FS.GG.Coord.GitHub.V2Projection

    /// A selected file preimage and desired bytes. Existing permissions are retained; new files use
    /// the declared executable mode. Apply rechecks both bytes and mode before any write.
    type FileChange =
        { Path: string
          Before: byte array option
          After: byte array
          Executable: bool
          BeforeMode: UnixFileMode option }

    /// Preview of one exact repository/project and its complete local change set. No board
    /// membership or planning mutation is represented by this value.
    type Plan = { Repository: string; ProjectId: string; Changes: FileChange list }

    /// Decode closed Binding v3 through the canonical decoder and require the selected repository
    /// and exact loaded adapter DLL digest. Organization and unsupported owner bindings refuse.
    val validate: repository: string -> adapterSha256: string -> bindingJson: string -> Result<Binding, string>

    /// Require a credential-free canonical HTTPS or SSH GitHub origin matching the selected product.
    val validateRemote: repository: string -> origin: string -> Result<unit, string>

    /// Invocation-start Git authority; fresh scaffold effects do not turn it into retained authority.
    type OriginAuthority = RetainedGit | FreshTarget

    /// Read Git metadata presence before scaffold effects without changing the target.
    val captureOriginAuthority: target: string -> OriginAuthority

    /// Require the selected credential-free origin. Only successful fresh scaffolds may have
    /// readable, newly initialized unborn Git without an origin; retained/retrofit paths refuse.
    val validateOrigin:
        authority: OriginAuthority -> scaffoldSucceeded: bool -> target: string -> repository: string ->
        runGit: (string list -> int * string) -> Result<unit, string>

    /// Read-only preparation from an immutable producer revision and injected raw-file transport.
    /// Verify manifest leaf digests, complete selected kit/driver directories and coherent adapter pin.
    /// Preserve unrelated JSON; reject authored conflicts, legacy wiring and incomplete publication.
    /// Selected binding, exact tool/consumer provenance and settings are staged before effects.
    val prepare:
        target: string -> repository: string -> kitRevision: string -> bindingJson: string ->
        fetch: (string -> Result<string, string>) -> Result<Plan, string>

    /// Recheck every preimage before local installation. Retain modes and restore prior file bytes
    /// on failure; incomplete rollback is reported explicitly. An empty plan performs no writes.
    /// This operation neither invokes the adapter nor creates resources or changes board fields.
    val apply: target: string -> plan: Plan -> Result<unit, string>
