namespace FS.GG.Coord.Cli

open System
open System.Text.Json

module internal SkillPrivateDurability =
    val syncDirectory: string -> unit

module SkillTelemetryReaders =
    type Coverage =
        | Unknown
        | Unsupported

    type ReaderError = { Message: string; Coverage: Coverage }

    type RepositoryIdentity = private RepositoryIdentity of string
    type CredentialReference = private CredentialReference of string

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
