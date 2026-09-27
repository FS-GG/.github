namespace FS.GG.Coord.GitHub

open FS.GG.Coord
open FS.GG.Coordination.GitHub

/// GitHub-side storage boundary for the dedicated retained intake aggregate. The port must read the
/// protected ref and publish the supplied objects with an atomic expected-head compare and swap.
module ProtectedIntakeJournal =
    type Read =
        { Repository: string
          RepositoryId: int64
          Ref: string
          Protection: ProtectionObservation
          FirstHead: string option
          SecondHead: string option
          Observation: JournalObservation
          CommitBytes: Map<string, byte[]>
          TreeBytes: Map<string, byte[]> }

    type Objects =
        { EventObjectId: string; EventBytes: byte[]
          HeadObjectId: string; HeadBytes: byte[]
          TreeObjectId: string; TreeBytes: byte[]
          CommitObjectId: string; CommitBytes: byte[] }

    type Proposal = { Cas: CasProposal; Objects: Objects }

    type CompareAndSwapResult =
        | Won
        | ParentConflict
        | Refused of string
        | ResponseUnknown

    type Port =
        { Read: AggregateAddress -> Read
          CompareAndSwap: Proposal -> CompareAndSwapResult }

    type Snapshot =
        { State: IntakeTransaction.State
          Head: JournalSnapshot }

    type AppendResult =
        | Appended of Snapshot
        | ObservedWithoutGrant of Snapshot
        | Conflict of Snapshot option
        | DefiniteRefusal of string
        | Indeterminate of string

    /// This address must resolve to refs/heads/fsgg/v2/journal/operation/13.
    val address: AggregateAddress
    /// Requires both protected-ref reads to name the same complete, valid journal head.
    val restore: Read -> Result<Snapshot, string>
    /// Plans one legal event against a fresh, stable snapshot; no journal creation is implied.
    val planAppend: operationId: string -> Read -> IntakeTransaction.Event -> Result<Proposal, string>
    /// Only a confirmed CAS win with exact readback grants this append attempt.
    val append: Port -> operationId: string -> IntakeTransaction.Event -> Result<AppendResult, string>

    type StrongAbsence =
        { OriginalRequestSha256: string
          EvidenceDigest: string
          Kind: string }

    type ProviderReconciliation =
        { Read: IntakeTransaction.Identity -> byte[] -> Result<StrongAbsence option, string> }

    /// The provider port must perform a fresh authoritative read. Evidence must name the exact retained
    /// original request. Unknown or missing evidence never authorizes another native create.
    val originalRequestAfterStrongAbsence:
        Snapshot -> IntakeTransaction.Identity -> ProviderReconciliation -> Result<byte[], string>
