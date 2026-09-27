namespace FS.GG.Coord.Cli

open System
open System.IO

module SkillConversionContract =
    type Coverage = Unknown | Unsupported
    type CommandError = { Message: string; ExitCode: int }
    type CommandResult = { ExitCode: int; Stdout: byte array; Stderr: byte array }

    type RepositoryIdentity = private RepositoryIdentity of string
    type CredentialReference = private CredentialReference of string
    type HostConfig =
        { Path: FileInfo
          StoreRoot: DirectoryInfo
          Engine: string
          Repository: RepositoryIdentity option
          Workspace: bool
          Producer: string option
          BindingDigest: string option
          CredentialReference: CredentialReference option }

    module Configuration =
        val canonicalRepository: string -> Result<RepositoryIdentity, CommandError>
        val discoverRepository: DirectoryInfo -> Result<RepositoryIdentity, CommandError>
        val discover: explicitPath: string option -> Result<HostConfig option, CommandError>
        val validateWorkspace: HostConfig -> Result<unit, CommandError>
        val mutationCommand: HostConfig -> string list -> Result<string list, CommandError>
        val createCiAssignment:
            HostConfig -> feature: string -> item: string -> attempt: string ->
            parentAttempt: string option -> producer: string -> Result<FileInfo, CommandError>

    type NativeCounters =
        { Input: int64; CachedInput: int64; Output: int64; Reasoning: int64; Total: int64 }
    type NativeTurn =
        { TurnId: Guid; Sequence: int; Provider: string; Model: string; Effort: string; Usage: NativeCounters }
    type NativeInventory =
        { ThreadId: Guid
          AllTurnIds: Guid list
          Turns: NativeTurn list
          Complete: bool
          Coverage: Coverage
          SourceDigest: string
          RosterDigest: string }

    module NativeUsage =
        val collect:
            parentThreadId: Guid -> nativeAgentId: string -> rootInvocationId: string ->
            invocationId: string -> revision: int -> Result<NativeInventory, CommandError>

    type TelemetryCommand =
        | Begin of feature: string * item: string * originalItem: string option * attempt: string *
            parentAttempt: string option * parentToken: string option * relation: string * producer: string *
            model: string * effort: string * lateAfterSeconds: int
        | PopulationOnly of feature: string * item: string * originalItem: string * producer: string
        | Started of token: string * nativeId: string
        | Finish of token: string * outcome: string * exitCode: int option
        | UsageReconcile of token: string
        | CiAssignment of feature: string * item: string * attempt: string * parentAttempt: string option * producer: string
        | Review of token: string * scope: string * input: FileInfo
        | Activity of token: string * input: FileInfo
        | UsageAttribution of token: string * input: FileInfo
        | Complication of token: string * input: FileInfo
        | Status

    module TelemetryAdapter =
        val run: HostConfig option -> TelemetryCommand -> CommandResult
        val acceptsStateSchema: string -> bool

    type EstimateDecision = InsufficientData | NotCostJustified | Uncertain | PilotCandidate
    type GraphDecision = Passed | Blocked of missingAncestors: string list

    module Preflight =
        val assess: ReadOnlyMemory<byte> -> Result<EstimateDecision * byte array, CommandError>
        val graph:
            yaml: ReadOnlyMemory<byte> -> requirements: string list ->
            Result<GraphDecision * byte array, CommandError>
