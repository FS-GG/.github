namespace FS.GG.Coord

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

module CycleLedger =
    type Unit =
        { Id: string
          Dependencies: string list
          Completed: bool
          Evidence: string list
          PlayerJourneyRequired: bool }
    type Ledger = { SourceRevision: string; Units: Unit list }
    type Cycle = { Id: string; UnitId: string; Executor: string; Repository: string; BaseCommit: string }
    type ProviderReceipt =
        { Schema: string; Provider: string; WorkId: string; CycleId: string; SourceRevision: string
          CandidateHead: string; Verdict: string; Round: int; PlayerJourney: bool option
          GeneratorId: string; GeneratorVersion: string; ArtifactDigest: string }
    type Evidence =
        { ImplementationHead: string; ReviewHead: string; FeedbackCycle: string; FeedbackActive: bool
          MergedPr: int option; MergeHead: string option; EvidencePaths: string list; Dispositions: string list }
    type UpdateReceipt =
        { Schema: string
          CycleId: string
          UnitId: string
          SourceRevision: string
          ImplementationHead: string
          ReviewHead: string
          FeedbackCycle: string
          FeedbackActive: bool
          MergedPr: int
          MergeHead: string
          EvidencePaths: string list
          Dispositions: string list
          EvidenceDigest: string }
    type Action = | Resume of Cycle | Register of Cycle | Advance of Cycle | Update of Cycle * UpdateReceipt | Escalate of Cycle | Complete

    let private required name value =
        if String.IsNullOrWhiteSpace value then Some $"%s{name} is required" else None

    let private jsonProperty (name: string) (node: JsonElement) =
        match node.TryGetProperty name with
        | true, value -> value
        | _ -> invalidArg name "is required"

    let private jsonText name node =
        let value = jsonProperty name node
        if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then
            invalidArg name "must be a non-empty string"
        value.GetString()

    let parseProviderReceipt (artifactBytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(artifactBytes))
            let root = document.RootElement
            let generator = jsonProperty "generator" root
            let roundNode = jsonProperty "round" root
            let round =
                match roundNode.TryGetInt32() with
                | true, value -> value
                | _ -> invalidArg "round" "must be an integer"
            let playerJourney =
                match (jsonProperty "playerJourney" root).ValueKind with
                | JsonValueKind.True -> Some true
                | JsonValueKind.False -> Some false
                | JsonValueKind.Null -> None
                | _ -> invalidArg "playerJourney" "must be boolean or null"
            Ok
                { Schema = jsonText "schema" root
                  Provider = jsonText "provider" root
                  WorkId = jsonText "workId" root
                  CycleId = jsonText "cycleId" root
                  SourceRevision = jsonText "sourceRevision" root
                  CandidateHead = jsonText "candidateHead" root
                  Verdict = jsonText "verdict" root
                  Round = round
                  PlayerJourney = playerJourney
                  GeneratorId = jsonText "id" generator
                  GeneratorVersion = jsonText "version" generator
                  ArtifactDigest = "sha256:" + (SHA256.HashData artifactBytes |> Convert.ToHexString).ToLowerInvariant() }
        with error -> Error [ "provider artifact is invalid: " + error.Message ]

    let cycleId unitId executor repository baseCommit =
        [ unitId; executor; repository; baseCommit ]
        |> String.concat "\n"
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun digest -> "cycle-" + digest.ToLowerInvariant().Substring(0, 16)

    let inspect (ledger: Ledger) =
        let ids = ledger.Units |> List.map _.Id
        let errors =
            [ yield! required "ledger source revision" ledger.SourceRevision |> Option.toList
              for unit in ledger.Units do
                  yield! required "unit id" unit.Id |> Option.toList
                  for dependency in unit.Dependencies do
                      if not (List.contains dependency ids) then yield $"unit %s{unit.Id} names unknown dependency %s{dependency}"
                      if dependency = unit.Id then yield $"unit %s{unit.Id} depends on itself"
              if ids |> List.distinct |> List.length <> ids.Length then yield "ledger unit ids must be unique"
              let rec reaches seen id =
                  if List.contains id seen then true
                  else ledger.Units |> List.tryFind (fun unit -> unit.Id = id) |> Option.exists (fun unit -> unit.Dependencies |> List.exists (reaches (id :: seen)))
              for unit in ledger.Units do
                  if unit.Dependencies |> List.exists (reaches [ unit.Id ]) then yield $"unit %s{unit.Id} participates in a dependency cycle" ]
        if not (List.isEmpty errors) then Error errors
        else
            let complete id = ledger.Units |> List.exists (fun unit -> unit.Id = id && unit.Completed)
            ledger.Units |> List.filter (fun unit -> not unit.Completed && (unit.Dependencies |> List.forall complete)) |> Ok

    let register (ledger: Ledger) executor repository baseCommit selectedUnit parallelAuthorized disjointTouchSets (live: Cycle list) =
        match inspect ledger with
        | Error errors -> Error errors
        | Ok ready ->
            match ready with
            | [] -> if ledger.Units |> List.forall _.Completed then Ok Complete else Error [ "no dependency-ready ledger unit exists" ]
            | [ unit ] ->
                let errors = [ yield! required "executor" executor |> Option.toList; yield! required "repository" repository |> Option.toList; yield! required "base commit" baseCommit |> Option.toList ]
                if not (List.isEmpty errors) then Error errors
                else
                    let id = cycleId unit.Id executor repository baseCommit
                    match live |> List.filter (fun cycle -> cycle.UnitId = unit.Id) with
                    | [] -> Ok(Register { Id = id; UnitId = unit.Id; Executor = executor; Repository = repository; BaseCommit = baseCommit })
                    | [ cycle ] when cycle.Id = id -> Ok(Resume cycle)
                    | _ -> Error [ $"unit %s{unit.Id} has an incompatible live cycle" ]
            | ready ->
                match selectedUnit |> Option.bind (fun id -> ready |> List.tryFind (fun unit -> unit.Id = id)) with
                | Some unit when parallelAuthorized && disjointTouchSets ->
                    let id = cycleId unit.Id executor repository baseCommit
                    match live |> List.filter (fun cycle -> cycle.UnitId = unit.Id) with
                    | [] -> Ok(Register { Id = id; UnitId = unit.Id; Executor = executor; Repository = repository; BaseCommit = baseCommit })
                    | [ cycle ] when cycle.Id = id -> Ok(Resume cycle)
                    | _ -> Error [ $"unit %s{unit.Id} has an incompatible live cycle" ]
                | _ -> Error [ "multiple dependency-ready units require selected unit, explicit operator authorization, and disjoint touch-sets" ]

    let validateProvider expectedWorkId (expectedCycle: Cycle) expectedSourceRevision expectedHead expectedProvider expectedSchema expectedGenerator (receipt: ProviderReceipt) =
        let errors =
            [ yield! required "provider schema" receipt.Schema |> Option.toList
              yield! required "provider" receipt.Provider |> Option.toList
              yield! required "provider generator id" receipt.GeneratorId |> Option.toList
              yield! required "provider generator version" receipt.GeneratorVersion |> Option.toList
              yield! required "provider artifact digest" receipt.ArtifactDigest |> Option.toList
              if receipt.Provider <> expectedProvider then yield $"provider receipt must be from %s{expectedProvider}"
              if receipt.Schema <> expectedSchema then yield $"provider receipt schema must be %s{expectedSchema}"
              if receipt.GeneratorId <> expectedGenerator then yield $"provider generator must be %s{expectedGenerator}"
              if receipt.ArtifactDigest.Length <> 71 then yield "provider artifact digest is not bound to exact artifact bytes"
              if receipt.GeneratorVersion <> "1.0.0" then yield $"provider generator version %s{receipt.GeneratorVersion} is unsupported"
              if receipt.WorkId <> expectedWorkId then yield "provider receipt work id does not match"
              if receipt.CycleId <> expectedCycle.Id then yield "provider receipt cycle id does not match"
              if receipt.SourceRevision <> expectedSourceRevision then yield "provider receipt source revision is stale"
              if receipt.CandidateHead <> expectedHead then yield "provider receipt candidate head does not match"
              if receipt.Verdict <> "pass" then yield "provider receipt verdict is not pass"
              if receipt.Round < 0 || receipt.Round > 10 then yield "provider receipt round is outside the supported range" ]
        if List.isEmpty errors then Ok () else Error errors

    let advance (ledger: Ledger) (cycle: Cycle) (implementation: ProviderReceipt) (review: ProviderReceipt) (feedback: ProviderReceipt) (evidence: Evidence) =
        let expected = evidence.ImplementationHead
        let validate provider schema generator receipt = validateProvider cycle.UnitId cycle ledger.SourceRevision expected provider schema generator receipt
        let journeyRequired =
            ledger.Units
            |> List.tryFind (fun unit -> unit.Id = cycle.UnitId)
            |> Option.map _.PlayerJourneyRequired
            |> Option.defaultValue false
        let errors =
            [ if cycle.BaseCommit <> ledger.SourceRevision then yield "cycle base commit is stale against ledger source revision"
              if cycle.Id <> cycleId cycle.UnitId cycle.Executor cycle.Repository cycle.BaseCommit then yield "cycle identity is not the stable source-bound identity"
              for provider, schema, generator, receipt in [ "fsgg-sdd", "fsgg.sdd.report/1", "FS.GG.SDD.Artifacts", implementation; "critique", "fsgg.critique.report/3", "FS.GG.Critique", review; "feedback", "fsgg.feedback.report/2", "FS.GG.Feedback", feedback ] do
                  match validate provider schema generator receipt with Error reasons -> yield! reasons | Ok () -> ()
              if evidence.ReviewHead <> expected then yield "review evidence head does not match implementation head"
              if evidence.FeedbackCycle <> cycle.Id then yield "feedback evidence cycle does not match"
              if not evidence.FeedbackActive then yield "feedback activation is missing"
              if evidence.MergedPr.IsNone || evidence.MergeHead <> Some expected then yield "merged evidence does not bind the implementation head"
              if journeyRequired && review.PlayerJourney <> Some true then yield "critique receipt is missing a passing player journey required by the ledger unit"
              if review.Round = 10 && review.Verdict <> "pass" then yield "tenth review round requires escalation rather than advancement" ]
        if review.Round = 10 && review.Verdict <> "pass" && errors |> List.forall (fun error -> error = "provider receipt verdict is not pass" || error = "tenth review round requires escalation rather than advancement") then Ok(Escalate cycle)
        elif List.isEmpty errors then Ok(Advance cycle) else Error errors

    let private updateDigest (cycle: Cycle) sourceRevision (evidence: Evidence) mergedPr mergeHead =
        [ "fsgg.coord.cycle-update/1"
          cycle.Id
          cycle.UnitId
          sourceRevision
          evidence.ImplementationHead
          evidence.ReviewHead
          evidence.FeedbackCycle
          string evidence.FeedbackActive
          string mergedPr
          mergeHead
          String.concat "\u001f" evidence.EvidencePaths
          String.concat "\u001f" evidence.Dispositions ]
        |> String.concat "\n"
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun digest -> "sha256:" + digest.ToLowerInvariant()

    let update (ledger: Ledger) (cycle: Cycle) (evidence: Evidence) =
        let errors =
            [ if cycle.BaseCommit <> ledger.SourceRevision then yield "guarded update cycle is stale against ledger source revision"
              if cycle.Id <> cycleId cycle.UnitId cycle.Executor cycle.Repository cycle.BaseCommit then yield "guarded update cycle identity is invalid"
              if evidence.MergedPr.IsNone || evidence.MergeHead <> Some evidence.ImplementationHead then yield "guarded update requires a merged head-bound pull request"
              if evidence.ReviewHead <> evidence.ImplementationHead || evidence.FeedbackCycle <> cycle.Id || not evidence.FeedbackActive then yield "guarded update evidence chain is incomplete"
              if List.isEmpty evidence.EvidencePaths || evidence.EvidencePaths |> List.exists String.IsNullOrWhiteSpace then yield "guarded update requires evidence paths"
              if List.isEmpty evidence.Dispositions || evidence.Dispositions |> List.exists String.IsNullOrWhiteSpace then yield "guarded update requires checkpoint and finding dispositions" ]
        if List.isEmpty errors then
            let mergedPr = evidence.MergedPr.Value
            let mergeHead = evidence.MergeHead.Value
            let receipt =
                { Schema = "fsgg.coord.cycle-update/1"
                  CycleId = cycle.Id
                  UnitId = cycle.UnitId
                  SourceRevision = ledger.SourceRevision
                  ImplementationHead = evidence.ImplementationHead
                  ReviewHead = evidence.ReviewHead
                  FeedbackCycle = evidence.FeedbackCycle
                  FeedbackActive = evidence.FeedbackActive
                  MergedPr = mergedPr
                  MergeHead = mergeHead
                  EvidencePaths = evidence.EvidencePaths
                  Dispositions = evidence.Dispositions
                  EvidenceDigest = updateDigest cycle ledger.SourceRevision evidence mergedPr mergeHead }
            Ok(Update(cycle, receipt))
        else Error errors

    let complete (ledger: Ledger) (accepted: Cycle list) (guardedUpdates: UpdateReceipt list) rollupCycleIds =
        match inspect ledger with
        | Error errors -> Error errors
        | Ok _ ->
            let acceptedUnits = accepted |> List.map _.UnitId
            let required = ledger.Units |> List.map _.Id
            let missing = required |> List.filter (fun id -> not (List.contains id acceptedUnits))
            let missingRollup = accepted |> List.map _.Id |> List.filter (fun id -> not (List.contains id rollupCycleIds))
            let unchecked = ledger.Units |> List.filter (fun unit -> not unit.Completed || List.isEmpty unit.Evidence) |> List.map _.Id
            let duplicateAccepted = acceptedUnits |> List.distinct |> List.length <> acceptedUnits.Length
            let invalidCycles =
                accepted |> List.filter (fun cycle ->
                    cycle.BaseCommit <> ledger.SourceRevision
                    || cycle.Id <> cycleId cycle.UnitId cycle.Executor cycle.Repository cycle.BaseCommit)
            let acceptedIds = accepted |> List.map _.Id |> Set.ofList
            let guardedIds = guardedUpdates |> List.map _.CycleId |> Set.ofList
            let invalidUpdates =
                guardedUpdates
                |> List.choose (fun receipt ->
                    match accepted |> List.tryFind (fun cycle -> cycle.Id = receipt.CycleId) with
                    | None -> Some receipt.CycleId
                    | Some cycle ->
                        let evidence =
                            { ImplementationHead = receipt.ImplementationHead
                              ReviewHead = receipt.ReviewHead
                              FeedbackCycle = receipt.FeedbackCycle
                              FeedbackActive = receipt.FeedbackActive
                              MergedPr = Some receipt.MergedPr
                              MergeHead = Some receipt.MergeHead
                              EvidencePaths = receipt.EvidencePaths
                              Dispositions = receipt.Dispositions }
                        match update ledger cycle evidence with
                        | Ok(Update(_, expected)) when expected = receipt -> None
                        | _ -> Some receipt.CycleId)
            if not (List.isEmpty unchecked) then Error [ "required units are not checked with evidence: " + String.concat ", " unchecked ]
            elif duplicateAccepted then Error [ "accepted cycles must cover each unit exactly once" ]
            elif not (List.isEmpty invalidCycles) then Error [ "accepted cycles are not source-bound registered cycle identities" ]
            elif not (List.isEmpty invalidUpdates) then Error [ "guarded update receipts do not verify against their merged evidence: " + String.concat ", " invalidUpdates ]
            elif acceptedIds <> guardedIds then Error [ "final roll-up requires one guarded update for every accepted cycle" ]
            elif not (List.isEmpty missing) then Error [ "required units are missing accepted cycles: " + String.concat ", " missing ]
            elif not (List.isEmpty missingRollup) then Error [ "accepted cycles are missing from roll-up: " + String.concat ", " missingRollup ]
            else Ok Complete
