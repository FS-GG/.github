namespace FS.GG.Telemetry.Host

open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub

module NativeDeliverySource =
    type Observation =
        {
            Repository: string
            PullRequest: int64
            ExpectedHead: string
            ObservedHead: string
            BaseRef: string
            BaseSha: string
            State: string
            MergeCommit: string option
            MergedAt: string option
            ResponseBody: string
            SourceDigest: string
        }

    val decodeResponse:
        candidate: TelemetryStoreApplication.NativeDeliveryCandidate ->
        responseBody: string ->
            Result<Observation, string list>

    val acquire:
        transport: Transport.ISinglePageGitHubTransport ->
        candidate: TelemetryStoreApplication.NativeDeliveryCandidate ->
            Result<Observation, string list>
