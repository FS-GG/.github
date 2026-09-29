namespace FS.GG.Telemetry.Host

open System
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open FS.GG.Coord
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

    let private text (parent: JsonElement) (name: string) =
        match parent.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String ->
            value.GetString() |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)
        | _ -> None

    let private optionalText (parent: JsonElement) (name: string) =
        match parent.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Null -> Some None
        | true, value when value.ValueKind = JsonValueKind.String ->
            value.GetString() |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not) |> Option.map Some
        | _ -> None

    let decodeResponse (candidate: TelemetryStoreApplication.NativeDeliveryCandidate) (responseBody: string) =
        try
            use document = JsonDocument.Parse responseBody
            let root = document.RootElement
            let number = root.GetProperty("number").GetInt64()
            let stateText = text root "state"
            let merged = root.GetProperty("merged").GetBoolean()
            let head = root.GetProperty("head")
            let baseValue = root.GetProperty("base")
            let observedHead = text head "sha"
            let baseRef = text baseValue "ref"
            let baseSha = text baseValue "sha"
            let baseRepository = text (baseValue.GetProperty("repo")) "full_name"
            let rawMergeCommit = optionalText root "merge_commit_sha"
            let mergedAt = optionalText root "merged_at"
            let sha (value: string) = Regex.IsMatch(value, "^[0-9a-f]{40}$")
            match stateText, observedHead, baseRef, baseSha, baseRepository, rawMergeCommit, mergedAt with
            | Some state, Some observed, Some targetRef, Some targetSha, Some repository,
              Some rawMerge, Some mergedTime when
                number = candidate.PullRequest && repository = candidate.Repository
                && observed = candidate.ExpectedHead && sha observed && sha targetSha
                && rawMerge |> Option.forall sha
                && (state = "open" || state = "closed") ->
                let derived =
                    if merged then
                        match rawMerge, mergedTime with
                        | Some commit, Some timestamp when state = "closed" ->
                            match DateTimeOffset.TryParse timestamp with
                            | true, _ -> Ok("merged", Some commit, Some timestamp)
                            | _ -> Error [ "native delivery merged time is invalid" ]
                        | _ -> Error [ "native delivery merged identity is incomplete" ]
                    elif mergedTime.IsSome then
                        Error [ "native delivery unmerged response carries merged time" ]
                    else
                        // GitHub may return a speculative test-merge SHA for an unmerged PR. It is
                        // retained in the exact response bytes but is not protected merge identity.
                        Ok((if state = "closed" then "closed-unmerged" else "open"), None, None)
                derived
                |> Result.map (fun (nativeState, commit, timestamp) ->
                    { Repository = repository; PullRequest = number
                      ExpectedHead = candidate.ExpectedHead; ObservedHead = observed
                      BaseRef = targetRef; BaseSha = targetSha; State = nativeState
                      MergeCommit = commit; MergedAt = timestamp; ResponseBody = responseBody
                      SourceDigest = CanonicalJson.sha256(Encoding.UTF8.GetBytes responseBody) })
            | _ -> Error [ "native delivery primary response is malformed or differs from candidate" ]
        with _ -> Error [ "native delivery primary response is malformed or differs from candidate" ]

    let acquire (transport: Transport.ISinglePageGitHubTransport)
                (candidate: TelemetryStoreApplication.NativeDeliveryCandidate) =
        let parts = candidate.Repository.Split('/')
        if parts.Length <> 2 || parts |> Array.exists String.IsNullOrWhiteSpace then
            Error [ "native delivery candidate repository is invalid" ]
        else
            let request: Transport.Request =
                { Method = "GET"
                  Path = $"repos/%s{Uri.EscapeDataString parts[0]}/%s{Uri.EscapeDataString parts[1]}/pulls/%d{candidate.PullRequest}"
                  Query = []
                  Body = Transport.NoBody
                  Budget = Transport.Rest
                  IfNoneMatch = None
                  Subject = $"%s{candidate.Repository}#%d{candidate.PullRequest} native delivery source" }
            match transport.SendSingle request with
            | Error _ -> Error [ "native delivery primary read failed" ]
            | Ok response when response.Status <> 200 || response.NextLink.IsSome ->
                Error [ "native delivery primary read refused" ]
            | Ok response -> decodeResponse candidate response.Body
