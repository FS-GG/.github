namespace FS.GG.Coord.Cli

open System.IO

module SkillTelemetryAdapter =
    type CommandResult =
        { ExitCode: int
          Stdout: byte array
          Stderr: byte array }

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

    val run: SkillTelemetryReaders.HostConfig option -> TelemetryCommand -> CommandResult
    val acceptsStateSchema: string -> bool
