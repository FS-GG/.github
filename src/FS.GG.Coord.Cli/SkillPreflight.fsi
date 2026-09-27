namespace FS.GG.Coord.Cli

open System

/// Bounded, read-only implementations of the pipeline-preflight skill commands.
module SkillPreflight =
    type CommandError = { Message: string; ExitCode: int }

    type EstimateDecision =
        | InsufficientData
        | NotCostJustified
        | Uncertain
        | PilotCandidate

    type GraphDecision =
        | Passed
        | Blocked of missingAncestors: string list

    /// Assess the advisory economics document. The returned bytes are the command's JSON payload.
    val assess: ReadOnlyMemory<byte> -> Result<EstimateDecision * byte array, CommandError>

    /// Check literal workflow dependency ancestry against independent requirements.
    val graph:
        yaml: ReadOnlyMemory<byte> -> requirements: string list ->
        Result<GraphDecision * byte array, CommandError>
