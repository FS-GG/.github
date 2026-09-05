namespace FS.GG.Coord

open System
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

module Qualification =
    [<Literal>]
    let InputSchema = "fsgg.qualification.input/1"

    [<Literal>]
    let ResultSchema = "fsgg.qualification.result/1"

    type OperationKind = Analyze | Verify | Ship | Hosted | FixedPoint | Mutation
    type ToolIdentity = { Id: string; Version: string; Sha256: string }
    type ExecutorIdentity = { Id: string; Role: string; ImplementationSha256: string }
    type OperationEvidence =
        { Id: string; Kind: OperationKind; SubjectRevision: string; Tool: ToolIdentity
          Executor: ExecutorIdentity; CommandSha256: string; ArtifactSha256: string list
          ResultSha256: string; ReplayResultSha256: string option; ExitCode: int; Refusal: string option }
    type Claim = { Id: string; SubjectRevision: string; RequiredKinds: OperationKind list; EvidenceIds: string list }
    type MutationEvidence =
        { Id: string; OperationId: string; ExpectedRefusal: string; ObservedRefusal: string
          ProductionImplementationSha256: string; FixtureImplementationSha256: string; FixtureExecutorId: string }
    type HostedCheck = { Scope: string; Id: string; SubjectRevision: string; State: string; Conclusion: string }
    type HostedObservation = { Complete: bool; Checks: HostedCheck list }
    type ObligationDeclaration = NoObligations | Obligations of ids: string list
    type ObligationObservation = { HeadSha: string; Declarations: ObligationDeclaration list }
    type SemanticReview = { SubjectRevision: string; Accepted: bool; Evidence: string }
    type Input =
        { Schema: string; Subject: string; SubjectRevision: string; CheckoutClean: bool
          ToolManifest: ToolIdentity list; Executor: ExecutorIdentity; Operations: OperationEvidence list
          Claims: Claim list; Mutations: MutationEvidence list; HostedObservations: HostedObservation list
          Obligations: ObligationObservation; SemanticReview: SemanticReview }

    type Finding =
        | InvalidSchema of expected: string * observed: string
        | InvalidSubject of string
        | DirtyCheckout
        | InvalidDigest of field: string * value: string
        | DuplicateIdentity of kind: string * id: string
        | MissingOperationKind of OperationKind
        | OperationOrderMismatch of expected: OperationKind list * observed: OperationKind list
        | UndeclaredTool of operationId: string * toolId: string
        | ToolIdentityMismatch of operationId: string * toolId: string
        | WrongExecutor of operationId: string * executorId: string
        | StaleSubject of evidenceId: string * observedRevision: string
        | FailedOperation of operationId: string * exitCode: int
        | UnexpectedRefusal of operationId: string
        | FixedPointMismatch of operationId: string
        | ClaimEvidenceMissing of claimId: string * evidenceId: string
        | ClaimEvidenceMismatch of claimId: string * evidenceId: string
        | InadequateClaimEvidence of claimId: string * kind: OperationKind
        | MutationOperationMissing of mutationId: string * operationId: string
        | MutationRefusalMismatch of mutationId: string
        | MutationFixtureNotIndependent of mutationId: string
        | HostedObservationIncomplete of index: int
        | HostedForeignSubject of checkId: string * observedRevision: string
        | HostedCheckPending of checkId: string
        | HostedSetNotConverged
        | ObligationHeadMismatch of observedHead: string
        | ObligationDeclarationMissing
        | ObligationDeclarationDuplicate of count: int
        | ObligationIdDuplicate of id: string
        | SemanticReviewMissing
        | SemanticReviewStale of observedRevision: string

    type Accepted =
        { Schema: string; Subject: string; SubjectRevision: string; ToolCount: int; OperationCount: int
          ClaimCount: int; MutationCount: int; HostedCheckCount: int; ObligationCount: int
          SemanticReviewEvidence: string; EvidenceDigest: string; Digest: string }

    let private shaPattern = Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)
    let private revisionPattern = Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)
    let private subjectPattern = Regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*$", RegexOptions.CultureInvariant)
    let private tokenPattern = Regex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)

    let private duplicates kind (values: string list) =
        values
        |> List.countBy id
        |> List.choose (fun (value, count) -> if String.IsNullOrWhiteSpace value || count > 1 then Some(DuplicateIdentity(kind, value)) else None)

    let private digest field (value: string) = if shaPattern.IsMatch value then [] else [ InvalidDigest(field, value) ]
    let private executorDigests prefix (executor: ExecutorIdentity) =
        [ if String.IsNullOrWhiteSpace executor.Id || String.IsNullOrWhiteSpace executor.Role then
              yield DuplicateIdentity("executor", executor.Id)
          yield! digest ($"%s{prefix}.implementationSha256") executor.ImplementationSha256 ]

    let private operationKindName = function
        | Analyze -> "analyze" | Verify -> "verify" | Ship -> "ship" | Hosted -> "hosted"
        | FixedPoint -> "fixed-point" | Mutation -> "mutation"

    let private operationKind (value: string) =
        match value with
        | "analyze" -> Analyze | "verify" -> Verify | "ship" -> Ship | "hosted" -> Hosted
        | "fixed-point" -> FixedPoint | "mutation" -> Mutation
        | other -> raise (FormatException($"operation kind '%s{other}' is unknown"))

    let private strictObject (label: string) (expected: string list) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then raise (FormatException($"%s{label} must be an object"))
        let expectedSet = Set.ofList expected
        let observed = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let missing = Set.difference expectedSet observed |> Set.toList
        let unknown = Set.difference observed expectedSet |> Set.toList
        let missingText = String.concat "," missing
        let unknownText = String.concat "," unknown
        if not missing.IsEmpty then raise (FormatException($"%s{label} is missing fields: %s{missingText}"))
        if not unknown.IsEmpty then raise (FormatException($"%s{label} has unknown fields: %s{unknownText}"))

    let private property (name: string) (element: JsonElement) = element.GetProperty name
    let private text (label: string) (name: string) (element: JsonElement) =
        let value = property name element
        if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then
            raise (FormatException($"%s{label}.%s{name} must be a non-empty string"))
        value.GetString()
    let private optionalText (label: string) (name: string) (element: JsonElement) =
        let value = property name element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.String when not (String.IsNullOrWhiteSpace(value.GetString())) -> Some(value.GetString())
        | _ -> raise (FormatException($"%s{label}.%s{name} must be null or a non-empty string"))
    let private boolean (label: string) (name: string) (element: JsonElement) =
        let value = property name element
        if value.ValueKind <> JsonValueKind.True && value.ValueKind <> JsonValueKind.False then
            raise (FormatException($"%s{label}.%s{name} must be boolean"))
        value.GetBoolean()
    let private integer (label: string) (name: string) (element: JsonElement) =
        let value = property name element
        match value.TryGetInt32() with
        | true, number -> number
        | _ -> raise (FormatException($"%s{label}.%s{name} must be a 32-bit integer"))
    let private array (label: string) (name: string) (element: JsonElement) =
        let value = property name element
        if value.ValueKind <> JsonValueKind.Array then raise (FormatException($"%s{label}.%s{name} must be an array"))
        value.EnumerateArray() |> List.ofSeq
    let private strings (label: string) (name: string) (element: JsonElement) =
        array label name element
        |> List.map (fun value ->
            if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then
                raise (FormatException($"%s{label}.%s{name} must contain non-empty strings"))
            value.GetString())

    let private parseTool (label: string) (element: JsonElement) =
        strictObject label [ "id"; "version"; "sha256" ] element
        { Id = text label "id" element; Version = text label "version" element; Sha256 = text label "sha256" element }

    let private parseExecutor (label: string) (element: JsonElement) =
        strictObject label [ "id"; "role"; "implementationSha256" ] element
        { Id = text label "id" element
          Role = text label "role" element
          ImplementationSha256 = text label "implementationSha256" element }

    let parseInput (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strictObject "input"
                [ "schema"; "subject"; "subjectRevision"; "checkoutClean"; "toolManifest"; "executor"
                  "operations"; "claims"; "mutations"; "hostedObservations"; "obligations"; "semanticReview" ] root
            let tools = array "input" "toolManifest" root |> List.mapi (fun index item -> parseTool ($"toolManifest[%d{index}]") item)
            let executor = parseExecutor "executor" (property "executor" root)
            let operations =
                array "input" "operations" root
                |> List.mapi (fun index item ->
                    let label = $"operations[%d{index}]"
                    strictObject label
                        [ "id"; "kind"; "subjectRevision"; "tool"; "executor"; "commandSha256"
                          "artifactSha256"; "resultSha256"; "replayResultSha256"; "exitCode"; "refusal" ] item
                    { Id = text label "id" item
                      Kind = text label "kind" item |> operationKind
                      SubjectRevision = text label "subjectRevision" item
                      Tool = parseTool ($"%s{label}.tool") (property "tool" item)
                      Executor = parseExecutor ($"%s{label}.executor") (property "executor" item)
                      CommandSha256 = text label "commandSha256" item
                      ArtifactSha256 = strings label "artifactSha256" item
                      ResultSha256 = text label "resultSha256" item
                      ReplayResultSha256 = optionalText label "replayResultSha256" item
                      ExitCode = integer label "exitCode" item
                      Refusal = optionalText label "refusal" item })
            let claims =
                array "input" "claims" root
                |> List.mapi (fun index item ->
                    let label = $"claims[%d{index}]"
                    strictObject label [ "id"; "subjectRevision"; "requiredKinds"; "evidenceIds" ] item
                    { Id = text label "id" item
                      SubjectRevision = text label "subjectRevision" item
                      RequiredKinds = strings label "requiredKinds" item |> List.map operationKind
                      EvidenceIds = strings label "evidenceIds" item })
            let mutations =
                array "input" "mutations" root
                |> List.mapi (fun index item ->
                    let label = $"mutations[%d{index}]"
                    strictObject label
                        [ "id"; "operationId"; "expectedRefusal"; "observedRefusal"; "productionImplementationSha256"
                          "fixtureImplementationSha256"; "fixtureExecutorId" ] item
                    { Id = text label "id" item
                      OperationId = text label "operationId" item
                      ExpectedRefusal = text label "expectedRefusal" item
                      ObservedRefusal = text label "observedRefusal" item
                      ProductionImplementationSha256 = text label "productionImplementationSha256" item
                      FixtureImplementationSha256 = text label "fixtureImplementationSha256" item
                      FixtureExecutorId = text label "fixtureExecutorId" item })
            let hosted =
                array "input" "hostedObservations" root
                |> List.mapi (fun observationIndex observation ->
                    let label = $"hostedObservations[%d{observationIndex}]"
                    strictObject label [ "complete"; "checks" ] observation
                    let checks =
                        array label "checks" observation
                        |> List.mapi (fun checkIndex check ->
                            let checkLabel = $"%s{label}.checks[%d{checkIndex}]"
                            strictObject checkLabel [ "scope"; "id"; "subjectRevision"; "state"; "conclusion" ] check
                            { Scope = text checkLabel "scope" check; Id = text checkLabel "id" check
                              SubjectRevision = text checkLabel "subjectRevision" check
                              State = text checkLabel "state" check; Conclusion = text checkLabel "conclusion" check })
                    { Complete = boolean label "complete" observation; Checks = checks })
            let obligationElement = property "obligations" root
            strictObject "obligations" [ "headSha"; "declarations" ] obligationElement
            let declarations =
                array "obligations" "declarations" obligationElement
                |> List.mapi (fun index declaration ->
                    let label = $"obligations.declarations[%d{index}]"
                    strictObject label [ "kind"; "ids" ] declaration
                    match text label "kind" declaration, strings label "ids" declaration with
                    | "none", [] -> NoObligations
                    | "some", ids when not ids.IsEmpty -> Obligations ids
                    | kind, _ -> raise (FormatException($"%s{label} has invalid kind/ids combination '%s{kind}'")))
            let semantic = property "semanticReview" root
            strictObject "semanticReview" [ "subjectRevision"; "accepted"; "evidence" ] semantic
            Ok
                { Schema = text "input" "schema" root
                  Subject = text "input" "subject" root
                  SubjectRevision = text "input" "subjectRevision" root
                  CheckoutClean = boolean "input" "checkoutClean" root
                  ToolManifest = tools
                  Executor = executor
                  Operations = operations
                  Claims = claims
                  Mutations = mutations
                  HostedObservations = hosted
                  Obligations = { HeadSha = text "obligations" "headSha" obligationElement; Declarations = declarations }
                  SemanticReview =
                    { SubjectRevision = text "semanticReview" "subjectRevision" semantic
                      Accepted = boolean "semanticReview" "accepted" semantic
                      Evidence = text "semanticReview" "evidence" semantic } }
        with
        | :? JsonException as error -> Error [ $"invalid qualification JSON: %s{error.Message}" ]
        | :? FormatException as error -> Error [ error.Message ]

    let private canonicalAccepted (accepted: Accepted) includeDigest =
        let payload =
            {| schema = accepted.Schema
               subject = accepted.Subject
               subjectRevision = accepted.SubjectRevision
               toolCount = accepted.ToolCount
               operationCount = accepted.OperationCount
               claimCount = accepted.ClaimCount
               mutationCount = accepted.MutationCount
               hostedCheckCount = accepted.HostedCheckCount
               obligationCount = accepted.ObligationCount
               semanticReviewEvidence = accepted.SemanticReviewEvidence
               evidenceDigest = accepted.EvidenceDigest
               digest = if includeDigest then accepted.Digest else "" |}
            |> JsonSerializer.SerializeToUtf8Bytes
        CanonicalJson.canonicalize payload |> Result.defaultWith invalidOp

    let canonicalResult accepted = canonicalAccepted accepted true + "\n"

    let validate (input: Input) =
        let findings = ResizeArray<Finding>()
        if input.Schema <> InputSchema then findings.Add(InvalidSchema(InputSchema, input.Schema))
        if not (subjectPattern.IsMatch input.Subject) then findings.Add(InvalidSubject input.Subject)
        if not (revisionPattern.IsMatch input.SubjectRevision) then findings.Add(InvalidDigest("subjectRevision", input.SubjectRevision))
        if not input.CheckoutClean then findings.Add DirtyCheckout
        findings.AddRange(executorDigests "executor" input.Executor)

        findings.AddRange(duplicates "tool" (input.ToolManifest |> List.map _.Id))
        for tool in input.ToolManifest do
            if not (tokenPattern.IsMatch tool.Id) || String.IsNullOrWhiteSpace tool.Version then
                findings.Add(DuplicateIdentity("tool", tool.Id))
            findings.AddRange(digest ($"tool[%s{tool.Id}].sha256") tool.Sha256)

        findings.AddRange(duplicates "operation" (input.Operations |> List.map _.Id))
        let tools = input.ToolManifest |> List.map (fun tool -> tool.Id, tool) |> Map.ofList
        let operations = input.Operations |> List.map (fun operation -> operation.Id, operation) |> Map.ofList
        let requiredOrder = [ Analyze; Verify; Ship; Hosted; FixedPoint ]
        for kind in requiredOrder do
            if input.Operations |> List.exists (fun operation -> operation.Kind = kind) |> not then
                findings.Add(MissingOperationKind kind)
        let observedOrder = input.Operations |> List.map _.Kind |> List.distinct
        let requiredObserved = observedOrder |> List.filter (fun kind -> List.contains kind requiredOrder)
        if requiredObserved <> requiredOrder then findings.Add(OperationOrderMismatch(requiredOrder, requiredObserved))

        for operation in input.Operations do
            if operation.SubjectRevision <> input.SubjectRevision then findings.Add(StaleSubject(operation.Id, operation.SubjectRevision))
            match tools.TryFind operation.Tool.Id with
            | None -> findings.Add(UndeclaredTool(operation.Id, operation.Tool.Id))
            | Some tool when tool <> operation.Tool -> findings.Add(ToolIdentityMismatch(operation.Id, operation.Tool.Id))
            | Some _ -> ()
            if operation.Executor <> input.Executor then findings.Add(WrongExecutor(operation.Id, operation.Executor.Id))
            findings.AddRange(digest ($"operation[%s{operation.Id}].commandSha256") operation.CommandSha256)
            findings.AddRange(digest ($"operation[%s{operation.Id}].resultSha256") operation.ResultSha256)
            operation.ArtifactSha256 |> List.iteri (fun index value -> findings.AddRange(digest ($"operation[%s{operation.Id}].artifactSha256[%d{index}]") value))
            match operation.Kind with
            | Mutation ->
                if operation.ExitCode = 0 then findings.Add(FailedOperation(operation.Id, operation.ExitCode))
                if operation.Refusal |> Option.exists (String.IsNullOrWhiteSpace >> not) |> not then findings.Add(UnexpectedRefusal operation.Id)
            | FixedPoint ->
                if operation.ExitCode <> 0 then findings.Add(FailedOperation(operation.Id, operation.ExitCode))
                if operation.Refusal.IsSome then findings.Add(UnexpectedRefusal operation.Id)
                if operation.ReplayResultSha256 <> Some operation.ResultSha256 then findings.Add(FixedPointMismatch operation.Id)
            | _ ->
                if operation.ExitCode <> 0 then findings.Add(FailedOperation(operation.Id, operation.ExitCode))
                if operation.Refusal.IsSome then findings.Add(UnexpectedRefusal operation.Id)
                if operation.ReplayResultSha256.IsSome then findings.Add(FixedPointMismatch operation.Id)

        findings.AddRange(duplicates "claim" (input.Claims |> List.map _.Id))
        for claim in input.Claims do
            if claim.SubjectRevision <> input.SubjectRevision then findings.Add(StaleSubject(claim.Id, claim.SubjectRevision))
            for evidenceId in claim.EvidenceIds do
                match operations.TryFind evidenceId with
                | None -> findings.Add(ClaimEvidenceMissing(claim.Id, evidenceId))
                | Some evidence when evidence.SubjectRevision <> claim.SubjectRevision -> findings.Add(ClaimEvidenceMismatch(claim.Id, evidenceId))
                | Some _ -> ()
            for kind in claim.RequiredKinds do
                let adequate =
                    claim.EvidenceIds
                    |> List.exists (fun evidenceId -> operations.TryFind evidenceId |> Option.exists (fun evidence -> evidence.Kind = kind && evidence.ExitCode = 0))
                if not adequate then findings.Add(InadequateClaimEvidence(claim.Id, kind))

        findings.AddRange(duplicates "mutation" (input.Mutations |> List.map _.Id))
        for mutation in input.Mutations do
            match operations.TryFind mutation.OperationId with
            | None -> findings.Add(MutationOperationMissing(mutation.Id, mutation.OperationId))
            | Some operation when operation.Kind <> Mutation -> findings.Add(MutationOperationMissing(mutation.Id, mutation.OperationId))
            | Some operation ->
                if operation.Refusal <> Some mutation.ObservedRefusal || mutation.ExpectedRefusal <> mutation.ObservedRefusal then
                    findings.Add(MutationRefusalMismatch mutation.Id)
            if mutation.ProductionImplementationSha256 <> input.Executor.ImplementationSha256
               || mutation.ProductionImplementationSha256 = mutation.FixtureImplementationSha256
               || mutation.FixtureExecutorId = input.Executor.Id
               || not (shaPattern.IsMatch mutation.FixtureImplementationSha256)
               || String.IsNullOrWhiteSpace mutation.FixtureExecutorId then
                findings.Add(MutationFixtureNotIndependent mutation.Id)

        if input.HostedObservations.Length < 2 then findings.Add HostedSetNotConverged
        input.HostedObservations |> List.iteri (fun index observation ->
            if not observation.Complete then findings.Add(HostedObservationIncomplete(index + 1))
            findings.AddRange(duplicates ($"hosted-observation-%d{index + 1}") (observation.Checks |> List.map (fun check -> $"%s{check.Scope}:%s{check.Id}")))
            for check in observation.Checks do
                if check.SubjectRevision <> input.SubjectRevision then findings.Add(HostedForeignSubject(check.Id, check.SubjectRevision))
                if check.State <> "completed" || String.IsNullOrWhiteSpace check.Conclusion then findings.Add(HostedCheckPending check.Id))
        if input.HostedObservations.Length >= 2 then
            let normalized observation =
                observation.Checks
                |> List.map (fun check -> check.Scope, check.Id, check.SubjectRevision, check.State, check.Conclusion)
                |> List.sort
            let terminal = input.HostedObservations |> List.rev |> List.take 2
            if normalized terminal[0] <> normalized terminal[1] then findings.Add HostedSetNotConverged

        if input.Obligations.HeadSha <> input.SubjectRevision then findings.Add(ObligationHeadMismatch input.Obligations.HeadSha)
        match input.Obligations.Declarations with
        | [] -> findings.Add ObligationDeclarationMissing
        | [ NoObligations ] -> ()
        | [ Obligations ids ] ->
            findings.AddRange(duplicates "obligation" ids)
            for id, count in List.countBy id ids do if count > 1 then findings.Add(ObligationIdDuplicate id)
        | declarations -> findings.Add(ObligationDeclarationDuplicate declarations.Length)

        if not input.SemanticReview.Accepted || String.IsNullOrWhiteSpace input.SemanticReview.Evidence then findings.Add SemanticReviewMissing
        if input.SemanticReview.SubjectRevision <> input.SubjectRevision then findings.Add(SemanticReviewStale input.SemanticReview.SubjectRevision)

        if findings.Count > 0 then Error(List.ofSeq findings) else
        let obligationCount =
            match input.Obligations.Declarations with
            | [ NoObligations ] -> 0
            | [ Obligations ids ] -> ids.Length
            | _ -> 0
        let frame (value: string) = $"%d{Encoding.UTF8.GetByteCount value}:%s{value}"
        let addList values = string (List.length values) :: values
        let toolValues (tool: ToolIdentity) = [ tool.Id; tool.Version; tool.Sha256 ]
        let executorValues (executor: ExecutorIdentity) = [ executor.Id; executor.Role; executor.ImplementationSha256 ]
        let operationValues (operation: OperationEvidence) =
            [ operation.Id; operationKindName operation.Kind; operation.SubjectRevision ]
            @ toolValues operation.Tool @ executorValues operation.Executor
            @ [ operation.CommandSha256 ] @ addList operation.ArtifactSha256
            @ [ operation.ResultSha256; operation.ReplayResultSha256 |> Option.defaultValue ""; string operation.ExitCode; operation.Refusal |> Option.defaultValue "" ]
        let claimValues (claim: Claim) =
            [ claim.Id; claim.SubjectRevision ] @ addList (claim.RequiredKinds |> List.map operationKindName) @ addList claim.EvidenceIds
        let mutationValues (mutation: MutationEvidence) =
            [ mutation.Id; mutation.OperationId; mutation.ExpectedRefusal; mutation.ObservedRefusal
              mutation.ProductionImplementationSha256; mutation.FixtureImplementationSha256; mutation.FixtureExecutorId ]
        let hostedValues (observation: HostedObservation) =
            [ string observation.Complete; string observation.Checks.Length ]
            @ (observation.Checks |> List.collect (fun check -> [ check.Scope; check.Id; check.SubjectRevision; check.State; check.Conclusion ]))
        let obligationValues =
            input.Obligations.Declarations
            |> List.collect (function NoObligations -> [ "none"; "0" ] | Obligations ids -> "some" :: addList ids)
        let evidenceValues =
            [ input.Schema; input.Subject; input.SubjectRevision; string input.CheckoutClean ]
            @ addList (input.ToolManifest |> List.collect toolValues)
            @ executorValues input.Executor
            @ addList (input.Operations |> List.collect operationValues)
            @ addList (input.Claims |> List.collect claimValues)
            @ addList (input.Mutations |> List.collect mutationValues)
            @ addList (input.HostedObservations |> List.collect hostedValues)
            @ [ input.Obligations.HeadSha ] @ addList obligationValues
            @ [ input.SemanticReview.SubjectRevision; string input.SemanticReview.Accepted; input.SemanticReview.Evidence ]
        let evidenceDigest = evidenceValues |> List.map frame |> String.concat "|" |> Encoding.UTF8.GetBytes |> CanonicalJson.sha256
        let draft =
            { Schema = ResultSchema; Subject = input.Subject; SubjectRevision = input.SubjectRevision
              ToolCount = input.ToolManifest.Length; OperationCount = input.Operations.Length
              ClaimCount = input.Claims.Length; MutationCount = input.Mutations.Length
              HostedCheckCount = input.HostedObservations |> List.last |> _.Checks.Length
              ObligationCount = obligationCount; SemanticReviewEvidence = input.SemanticReview.Evidence
              EvidenceDigest = evidenceDigest; Digest = "" }
        let digest = canonicalAccepted draft false |> Encoding.UTF8.GetBytes |> CanonicalJson.sha256
        Ok { draft with Digest = digest }
