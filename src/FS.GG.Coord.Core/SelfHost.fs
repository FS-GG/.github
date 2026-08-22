namespace FS.GG.Coord

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

module SelfHost =
    [<RequireQualifiedAccess>]
    type BootstrapReason =
        | NewSchemaCase
        | RelocatedDecisionBoundary

    type Evidence =
        { Build: string
          Unit: string
          FocusedProductionRoute: string
          Provenance: string
          Inversion: string }

    type HostAcceptance =
        { Actor: string
          AcceptedAt: DateTimeOffset }

    type SelfHostBootstrapReceipt =
        { BaseSha: string
          CandidateHeadSha: string
          CandidateBinarySha256: string
          CandidateVersion: string
          SharedRefusal: string
          SnapshotSha256: string
          Reason: BootstrapReason
          Evidence: Evidence
          CandidateDecisionKey: string
          CandidateActionKey: string
          HostAcceptance: HostAcceptance
          Digest: string }

    type Replay =
        { DecisionKey: string
          ActionKey: string }

    type SelfHostReplayReceipt =
        { BootstrapDigest: string
          SnapshotSha256: string
          DecisionKey: string
          ActionKey: string
          ReplayedAt: DateTimeOffset
          Digest: string }

    type ReplayState =
        | NoBootstrap
        | ReplayRequired of SelfHostBootstrapReceipt
        | VerifiedReplay of SelfHostReplayReceipt
        | InvalidReplay of errors: string list

    [<Literal>]
    let ReceiptMarker = "<!-- fsgg:self-host-bootstrap/v1 -->"

    [<Literal>]
    let ReplayReceiptMarker = "<!-- fsgg:self-host-replay/v1 -->"

    let private reasonName = function
        | BootstrapReason.NewSchemaCase -> "new-schema-case"
        | BootstrapReason.RelocatedDecisionBoundary -> "relocated-decision-boundary"

    let private tryReason = function
        | "new-schema-case" -> Ok BootstrapReason.NewSchemaCase
        | "relocated-decision-boundary" -> Ok BootstrapReason.RelocatedDecisionBoundary
        | value -> Error [ $"unknown self-host bootstrap reason '%s{value}'" ]

    let private canonical receipt =
        [ receipt.BaseSha
          receipt.CandidateHeadSha
          receipt.CandidateBinarySha256.ToLowerInvariant()
          receipt.CandidateVersion
          receipt.SharedRefusal
          receipt.SnapshotSha256.ToLowerInvariant()
          reasonName receipt.Reason
          receipt.Evidence.Build
          receipt.Evidence.Unit
          receipt.Evidence.FocusedProductionRoute
          receipt.Evidence.Provenance
          receipt.Evidence.Inversion
          receipt.CandidateDecisionKey
          receipt.CandidateActionKey
          receipt.HostAcceptance.Actor
          receipt.HostAcceptance.AcceptedAt.ToUniversalTime().ToString("O") ]
        |> String.concat "\n"

    let private digest receipt =
        canonical receipt
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private validate receipt =
        let required name value =
            if String.IsNullOrWhiteSpace value then Some $"%s{name} is required" else None
        let errors =
            [ required "baseSha" receipt.BaseSha
              required "candidateHeadSha" receipt.CandidateHeadSha
              required "candidateVersion" receipt.CandidateVersion
              required "sharedRefusal" receipt.SharedRefusal
              required "build evidence" receipt.Evidence.Build
              required "unit evidence" receipt.Evidence.Unit
              required "focused production-route evidence" receipt.Evidence.FocusedProductionRoute
              required "provenance evidence" receipt.Evidence.Provenance
              required "inversion evidence" receipt.Evidence.Inversion
              required "candidateDecisionKey" receipt.CandidateDecisionKey
              required "candidateActionKey" receipt.CandidateActionKey
              required "host acceptance actor" receipt.HostAcceptance.Actor
              if receipt.HostAcceptance.AcceptedAt = DateTimeOffset.MinValue then
                  Some "host acceptance timestamp is required"
              else None
              if receipt.CandidateBinarySha256.Length <> 64
                 || receipt.CandidateBinarySha256 |> Seq.exists (fun c -> not (Uri.IsHexDigit c)) then
                  Some "candidateBinarySha256 must be exactly 64 hexadecimal characters"
              else None ]
            |> List.choose id
        let errors =
            if receipt.SnapshotSha256.Length <> 64
               || receipt.SnapshotSha256 |> Seq.exists (fun c -> not (Uri.IsHexDigit c)) then
                "snapshotSha256 must be exactly 64 hexadecimal characters" :: errors
            else errors
        if List.isEmpty errors then Ok () else Error errors

    let authorizeWrite receipt =
        match validate receipt with
        | Error errors -> Error errors
        | Ok () ->
            let expected = digest { receipt with Digest = "" }
            if String.Equals(expected, receipt.Digest, StringComparison.Ordinal) then Ok ()
            else Error [ "self-host bootstrap receipt digest does not match its bound fields" ]

    let createReceipt baseSha candidateHeadSha candidateBinarySha256 candidateVersion sharedRefusal snapshotSha256 reason evidence candidateDecisionKey candidateActionKey hostAcceptance =
        let unsigned =
            { BaseSha = baseSha
              CandidateHeadSha = candidateHeadSha
              CandidateBinarySha256 = candidateBinarySha256
              CandidateVersion = candidateVersion
              SharedRefusal = sharedRefusal
              SnapshotSha256 = snapshotSha256
              Reason = reason
              Evidence = evidence
              CandidateDecisionKey = candidateDecisionKey
              CandidateActionKey = candidateActionKey
              HostAcceptance = hostAcceptance
              Digest = "" }
        match validate unsigned with
        | Error errors -> Error errors
        | Ok () -> Ok { unsigned with Digest = digest unsigned }

    let verifyReplay (receipt: SelfHostBootstrapReceipt) (replay: Replay) =
        authorizeWrite receipt
        |> Result.bind (fun () ->
            [ if replay.DecisionKey <> receipt.CandidateDecisionKey then
                  yield "post-merge shared-engine decision key disagrees with the candidate receipt"
              if replay.ActionKey <> receipt.CandidateActionKey then
                  yield "post-merge shared-engine action key disagrees with the candidate receipt" ]
            |> function [] -> Ok () | errors -> Error errors)

    let private replayCanonical (receipt: SelfHostReplayReceipt) =
        [ receipt.BootstrapDigest.ToLowerInvariant()
          receipt.SnapshotSha256.ToLowerInvariant()
          receipt.DecisionKey
          receipt.ActionKey
          receipt.ReplayedAt.ToUniversalTime().ToString("O") ]
        |> String.concat "\n"

    let private replayDigest (receipt: SelfHostReplayReceipt) =
        replayCanonical receipt
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    let private validSha (value: string) =
        not (isNull value)
        && value.Length = 64
        && (value |> Seq.forall Uri.IsHexDigit)

    let private validateReplayReceipt (receipt: SelfHostReplayReceipt) =
        [ if not (validSha receipt.BootstrapDigest) then
              yield "bootstrapDigest must be exactly 64 hexadecimal characters"
          if not (validSha receipt.SnapshotSha256) then
              yield "snapshotSha256 must be exactly 64 hexadecimal characters"
          if String.IsNullOrWhiteSpace receipt.DecisionKey then yield "decisionKey is required"
          if String.IsNullOrWhiteSpace receipt.ActionKey then yield "actionKey is required"
          if receipt.ReplayedAt = DateTimeOffset.MinValue then yield "replayedAt is required" ]
        |> function [] -> Ok () | errors -> Error errors

    let verifyReplayReceipt (bootstrap: SelfHostBootstrapReceipt) (receipt: SelfHostReplayReceipt) =
        authorizeWrite bootstrap
        |> Result.bind (fun () -> validateReplayReceipt receipt)
        |> Result.bind (fun () ->
            let expected = replayDigest { receipt with Digest = "" }
            if not (String.Equals(expected, receipt.Digest, StringComparison.Ordinal)) then
                Error [ "self-host replay receipt digest does not match its bound fields" ]
            elif not (String.Equals(bootstrap.Digest, receipt.BootstrapDigest, StringComparison.Ordinal)) then
                Error [ "self-host replay receipt does not name the durable bootstrap authority" ]
            elif not (String.Equals(bootstrap.SnapshotSha256, receipt.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) then
                Error [ "post-merge shared-engine replay used a different snapshot" ]
            else
                verifyReplay bootstrap { DecisionKey = receipt.DecisionKey; ActionKey = receipt.ActionKey })

    let createReplayReceipt (bootstrap: SelfHostBootstrapReceipt) snapshotSha256 (replay: Replay) replayedAt =
        let unsigned: SelfHostReplayReceipt =
            { BootstrapDigest = bootstrap.Digest
              SnapshotSha256 = snapshotSha256
              DecisionKey = replay.DecisionKey
              ActionKey = replay.ActionKey
              ReplayedAt = replayedAt
              Digest = "" }
        verifyReplay bootstrap replay
        |> Result.bind (fun () -> validateReplayReceipt unsigned)
        |> Result.bind (fun () ->
            let receipt = { unsigned with Digest = replayDigest unsigned }
            verifyReplayReceipt bootstrap receipt |> Result.map (fun () -> receipt))

    let encodeReceipt receipt =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteString("baseSha", receipt.BaseSha)
        writer.WriteString("candidateHeadSha", receipt.CandidateHeadSha)
        writer.WriteString("candidateBinarySha256", receipt.CandidateBinarySha256)
        writer.WriteString("candidateVersion", receipt.CandidateVersion)
        writer.WriteString("sharedRefusal", receipt.SharedRefusal)
        writer.WriteString("snapshotSha256", receipt.SnapshotSha256)
        writer.WriteString("reason", reasonName receipt.Reason)
        writer.WriteStartObject("evidence")
        writer.WriteString("build", receipt.Evidence.Build)
        writer.WriteString("unit", receipt.Evidence.Unit)
        writer.WriteString("focusedProductionRoute", receipt.Evidence.FocusedProductionRoute)
        writer.WriteString("provenance", receipt.Evidence.Provenance)
        writer.WriteString("inversion", receipt.Evidence.Inversion)
        writer.WriteEndObject()
        writer.WriteString("candidateDecisionKey", receipt.CandidateDecisionKey)
        writer.WriteString("candidateActionKey", receipt.CandidateActionKey)
        writer.WriteStartObject("hostAcceptance")
        writer.WriteString("actor", receipt.HostAcceptance.Actor)
        writer.WriteString("acceptedAt", receipt.HostAcceptance.AcceptedAt.ToUniversalTime())
        writer.WriteEndObject()
        writer.WriteString("digest", receipt.Digest)
        writer.WriteEndObject()
        writer.Flush()
        ReceiptMarker + "\n" + Encoding.UTF8.GetString(stream.ToArray())

    let tryDecodeReceipt (body: string) =
        if not (body.StartsWith(ReceiptMarker, StringComparison.Ordinal)) then Ok None
        else
            try
                use document = JsonDocument.Parse(body.Substring(ReceiptMarker.Length).TrimStart())
                let root = document.RootElement
                let text (name: string) (value: JsonElement) = value.GetProperty(name).GetString()
                let evidence = root.GetProperty "evidence"
                let acceptance = root.GetProperty "hostAcceptance"
                match tryReason (text "reason" root) with
                | Error errors -> Error errors
                | Ok reason ->
                    let receipt =
                        { BaseSha = text "baseSha" root
                          CandidateHeadSha = text "candidateHeadSha" root
                          CandidateBinarySha256 = text "candidateBinarySha256" root
                          CandidateVersion = text "candidateVersion" root
                          SharedRefusal = text "sharedRefusal" root
                          SnapshotSha256 = text "snapshotSha256" root
                          Reason = reason
                          Evidence =
                            { Build = text "build" evidence
                              Unit = text "unit" evidence
                              FocusedProductionRoute = text "focusedProductionRoute" evidence
                              Provenance = text "provenance" evidence
                              Inversion = text "inversion" evidence }
                          CandidateDecisionKey = text "candidateDecisionKey" root
                          CandidateActionKey = text "candidateActionKey" root
                          HostAcceptance =
                            { Actor = text "actor" acceptance
                              AcceptedAt = acceptance.GetProperty("acceptedAt").GetDateTimeOffset() }
                          Digest = text "digest" root }
                    authorizeWrite receipt |> Result.map (fun () -> Some receipt)
            with ex ->
                Error [ "invalid self-host bootstrap receipt: " + ex.Message ]

    let encodeReplayReceipt receipt =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writer.WriteStartObject()
        writer.WriteString("bootstrapDigest", receipt.BootstrapDigest)
        writer.WriteString("snapshotSha256", receipt.SnapshotSha256)
        writer.WriteString("decisionKey", receipt.DecisionKey)
        writer.WriteString("actionKey", receipt.ActionKey)
        writer.WriteString("replayedAt", receipt.ReplayedAt.ToUniversalTime())
        writer.WriteString("digest", receipt.Digest)
        writer.WriteEndObject()
        writer.Flush()
        ReplayReceiptMarker + "\n" + Encoding.UTF8.GetString(stream.ToArray())

    let tryDecodeReplayReceipt (body: string) =
        if not (body.StartsWith(ReplayReceiptMarker, StringComparison.Ordinal)) then Ok None
        else
            try
                use document = JsonDocument.Parse(body.Substring(ReplayReceiptMarker.Length).TrimStart())
                let root = document.RootElement
                let text (name: string) = root.GetProperty(name).GetString()
                let receipt: SelfHostReplayReceipt =
                    { BootstrapDigest = text "bootstrapDigest"
                      SnapshotSha256 = text "snapshotSha256"
                      DecisionKey = text "decisionKey"
                      ActionKey = text "actionKey"
                      ReplayedAt = root.GetProperty("replayedAt").GetDateTimeOffset()
                      Digest = text "digest" }
                validateReplayReceipt receipt
                |> Result.bind (fun () ->
                    let expected = replayDigest { receipt with Digest = "" }
                    if String.Equals(expected, receipt.Digest, StringComparison.Ordinal) then Ok(Some receipt)
                    else Error [ "self-host replay receipt digest does not match its bound fields" ])
            with ex ->
                Error [ "invalid self-host replay receipt: " + ex.Message ]

    let replayState (comments: string list) =
        let bootstraps =
            comments
            |> List.filter (fun body -> body.StartsWith(ReceiptMarker, StringComparison.Ordinal))
            |> List.map tryDecodeReceipt
        let replays =
            comments
            |> List.filter (fun body -> body.StartsWith(ReplayReceiptMarker, StringComparison.Ordinal))
            |> List.map tryDecodeReplayReceipt
        let errors =
            [ yield! bootstraps |> List.choose (function Error values -> Some values | _ -> None) |> List.collect id
              yield! replays |> List.choose (function Error values -> Some values | _ -> None) |> List.collect id ]
        let bootstrapReceipts = bootstraps |> List.choose (function Ok(Some value) -> Some value | _ -> None)
        let replayReceipts = replays |> List.choose (function Ok(Some value) -> Some value | _ -> None)
        match errors, bootstrapReceipts, replayReceipts with
        | _ :: _, _, _ -> InvalidReplay errors
        | [], [], [] -> NoBootstrap
        | [], [], _ -> InvalidReplay [ "self-host replay evidence exists without bootstrap authority" ]
        | [], _ :: _ :: _, _ -> InvalidReplay [ "more than one self-host bootstrap receipt exists" ]
        | [], [ bootstrap ], [] -> ReplayRequired bootstrap
        | [], [ _ ], _ :: _ :: _ -> InvalidReplay [ "more than one self-host replay receipt exists" ]
        | [], [ bootstrap ], [ replay ] ->
            match verifyReplayReceipt bootstrap replay with
            | Ok () -> VerifiedReplay replay
            | Error values -> InvalidReplay values
