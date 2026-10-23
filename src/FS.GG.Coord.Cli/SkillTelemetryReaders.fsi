namespace FS.GG.Coord.Cli

open System
open System.Text.Json

module internal SkillPrivateDurability =
    val syncDirectory: string -> unit
    val withLock: string -> (unit -> 'a) -> 'a

module SkillTelemetryReaders =
    type Coverage =
        | Unknown
        | Unsupported

    type ReaderError = { Message: string; Coverage: Coverage }

    type RepositoryIdentity = private RepositoryIdentity of string
    type CredentialReference = private CredentialReference of string

    type WorkspaceDestination = private LocalWorkspace of storeRoot: string | RemoteWorkspace

    type HostConfig =
        {
            Path: string
            StoreRoot: string
            Engine: string
            Repository: RepositoryIdentity option
            Workspace: bool
            Producer: string option
            BindingDigest: string option
            CredentialReference: CredentialReference option
            Destination: WorkspaceDestination option
        }

    type Assignment =
        {
            Schema: string
            FeatureId: string
            ItemId: string
            AttemptId: string
            ParentAttemptId: string option
            ProducerStream: string
        }

    module Configuration =
        val repositoryValue: RepositoryIdentity -> string
        val canonicalRepository: string -> Result<RepositoryIdentity, ReaderError>
        val discoverRepository: cwd: string -> Result<RepositoryIdentity, ReaderError>
        val discover: explicitPath: string option -> Result<HostConfig option, ReaderError>
        val validateWorkspace: HostConfig -> Result<unit, ReaderError>
        val mutationCommand: HostConfig -> string list -> Result<string list, ReaderError>

        val assignment:
            schema: string ->
            feature: string ->
            item: string ->
            attempt: string ->
            parentAttempt: string option ->
            producer: string ->
                Result<Assignment, ReaderError>

        val createCiAssignment:
            HostConfig ->
            feature: string ->
            item: string ->
            attempt: string ->
            parentAttempt: string option ->
            producer: string ->
                Result<string, ReaderError>

    type ObservedCounters =
        { Input: int64 option
          CachedInput: int64 option
          CacheWriteInput: int64 option
          Output: int64 option
          Reasoning: int64 option
          Total: int64 option }

    type PassiveNativeRecord =
        { ThreadId: Guid
          TurnId: Guid
          ResponseId: string
          Usage: ObservedCounters
          Provider: string option
          Model: string option
          Effort: string option
          OccurredAt: string option
          SourceDigest: string
          Position: int64
          /// codex-local-rollout/1 or claude-local-transcript/1; selects the native identity namespace.
          Provenance: string }

    type PassiveCursor =
        { SourceInstance: string
          Generation: string
          Offset: int64
          AnchorDigest: string
          SkippingOversized: bool
          /// Claude prompt identity in force at Offset; None when unknown or for Codex rollouts.
          Context: string option }

    /// The explicitly selected private root that a passive source file must lie below.
    type PassiveSourceRoot =
        /// Codex home; sources lie below its sessions directory.
        | CodexHome of string
        /// Claude Code configuration home; sources lie below its projects directory.
        | ClaudeHome of string

    type PassiveScan =
        { Records: PassiveNativeRecord list
          Cursor: PassiveCursor
          ScannedBytes: int
          Diagnostics: string list
          /// Length of the opened descriptor when the pass ended; a cursor at this offset has read the whole file.
          SourceLength: int64
          /// Modification time of the opened descriptor, in Unix seconds, used only to recognize a quiescent source.
          ModifiedAtUnixSeconds: int64 }

    type NativeCounters =
        {
            Input: int64
            CachedInput: int64
            Output: int64
            Reasoning: int64
            Total: int64
        }

    type NativeTurn =
        {
            TurnId: Guid
            Sequence: int
            Provider: string
            Model: string
            Effort: string
            Usage: NativeCounters
        }

    type NativeInventoryRow =
        {
            TurnId: Guid
            Sequence: int
            Status: string
            Terminal: bool
            UsageAvailable: bool
        }

    type NativeInventory =
        {
            ThreadId: Guid
            AllTurnIds: Guid list
            TurnInventory: NativeInventoryRow list
            Turns: NativeTurn list
            Complete: bool
            Coverage: Coverage
            Provider: string option
            Model: string option
            Effort: string option
            SourceDigest: string
            RosterDigest: string
            InventoryPaging: JsonElement
            InventoryCapturedAt: string
            SourceBinding: JsonElement
            AppServerResponses: JsonElement
            RolloutRecords: JsonElement
            ProviderProvenance: string option
        }

    module NativeUsage =
        [<Literal>]
        val PassiveMaximumFiles: int = 8
        [<Literal>]
        val PassiveMaximumScanBytes: int = 4194304
        [<Literal>]
        val PassiveMaximumObservations: int = 64

        [<Literal>]
        val CodexPassiveProvenance: string = "codex-local-rollout/1"
        [<Literal>]
        val ClaudePassiveProvenance: string = "claude-local-transcript/1"

        val passiveIdentity: thread: Guid -> turn: Guid -> response: string -> string
        val passiveClaudeIdentity: thread: Guid -> turn: Guid -> response: string -> string
        val passiveRecordIdentity: record: PassiveNativeRecord -> string
        val scanPassiveFrom:
            sourceRoot: PassiveSourceRoot -> path: string -> prior: PassiveCursor option -> maximumBytes: int -> maximumRecords: int ->
                Result<PassiveScan, ReaderError>
        val scanPassive:
            codexHome: string -> path: string -> prior: PassiveCursor option -> maximumBytes: int -> maximumRecords: int ->
                Result<PassiveScan, ReaderError>

        val coverageForParent: Guid option -> Coverage

        val collect:
            parentThreadId: Guid ->
            nativeAgentId: string ->
            rootInvocationId: string ->
            invocationId: string ->
            revision: int ->
                Result<NativeInventory, ReaderError>

        val collectWith:
            command: string ->
            codexHome: string ->
            parentThreadId: Guid ->
            nativeAgentId: string ->
            rootInvocationId: string ->
            invocationId: string ->
            revision: int ->
                Result<NativeInventory, ReaderError>

        val collectProtectedWith:
            command: string ->
            codexHome: string ->
            parentThreadId: Guid ->
            nativeAgentId: string ->
            rootInvocationId: string ->
            invocationId: string ->
            revision: int ->
                Result<NativeInventory, ReaderError>
