namespace FS.GG.Coord

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module RoadmapWorkUnit =
    [<Literal>]
    let PreparationInputSchema = "fsgg.roadmap-unit.preparation-input/1"
    [<Literal>]
    let PreparationPlanSchema = "fsgg.roadmap-unit.preparation-plan/1"
    [<Literal>]
    let PreparationApplicationSchema = "fsgg.roadmap-unit.preparation-application/1"
    [<Literal>]
    let RevisionBindingSchema = "fsgg.roadmap-unit.revision-binding/1"
    [<Literal>]
    let AcceptanceInputSchema = "fsgg.roadmap-unit.acceptance-input/1"
    [<Literal>]
    let EvidenceIndexSchema = "fsgg.roadmap-unit.evidence-index/1"

    type UnitState = Accepted | Unchecked
    type CatalogRow =
        { UnitId: string; Title: string; State: UnitState; Prerequisite: string option
          Gates: string list; EvidenceObligations: string list; ContractSha256: string }
    type RoadmapRow = { UnitId: string; Title: string; Prerequisite: string option; Gates: string list }
    type AuthorityPin = { RoadmapRevision: string; RoadmapDigest: string; CatalogDigest: string; Issue: string }
    type Registration = { Id: string; Kind: string; Draft: Intake.Draft }
    type PreparationInput =
        { Schema: string; RoadmapRevision: string; RoadmapSourceDigest: string; CatalogSourceDigest: string; Catalog: CatalogRow list; RoadmapRow: RoadmapRow
          AuthorityIssue: string; SddWorkId: string; RegistrationOwner: string; RegistrationRepository: string
          RegistrationPaths: string list }
    type PreparationRequest =
        { Schema: string; RoadmapRevision: string; AuthorityIssue: string; SddWorkId: string; RegistrationOwner: string
          RegistrationRepository: string; RegistrationPaths: string list }
    type PreparationPlan =
        { Schema: string; Unit: CatalogRow; AcceptedPrerequisite: string; Authority: AuthorityPin
          SddWorkId: string; Registrations: Registration list; GateRegistrations: string list
          EvidenceObligations: string list; Digest: string }
    type AppliedRegistration =
        { Id: string; Kind: string; DraftSha256: string; Issue: string; IssueUrl: string }
    type PreparationApplication =
        { Schema: string; UnitId: string; PlanDigest: string
          Registrations: AppliedRegistration list; Digest: string }
    type SddObservation =
        { Stage: string; SubjectRevision: string; ArtifactJson: string }
    type RevisionIdentities =
        { ImplementationPullRequest: int; ImplementationCandidate: string; ImplementationMerge: string
          AcceptancePullRequest: int; AcceptanceCandidate: string; AcceptanceMerge: string; ProtectedMain: string }
    type RevisionBinding =
        { Schema: string; Repository: string; Candidate: string; Merge: string
          CandidateTree: string; MergeTree: string; CommandSha256: string; ExitCode: int; Digest: string }
    type AcceptanceInput =
        { Schema: string; Plan: PreparationPlan; PreparationApplication: PreparationApplication
          Qualification: Qualification.Accepted
          LifecycleRunId: string; LifecycleUnitId: string; LifecycleLog: string
          RequiredLifecyclePhases: string list; LifecycleUsageReceipts: string list; LifecycleHistoryReport: string
          ReviewEvidence: string; StructuredReviewEvidence: string; ReviewCycleId: string; ReviewReceipt: string; SddWorkId: string
          SddObservations: SddObservation list; Identities: RevisionIdentities
          ImplementationBinding: RevisionBinding; AcceptanceBinding: RevisionBinding; AcceptedAt: string }
    type EvidenceEntry = { Name: string; Sha256: string; Source: string }
    type AcceptanceCandidate = private AcceptanceCandidate of AcceptanceInput
    type ObservedAcceptance = private ObservedAcceptance of AcceptanceInput
    type Accepted = private { ReceiptJson: string; EvidenceIndexJson: string; BundleJson: string; Digest: string }
    type Finding =
        | InvalidSchema of expected: string * observed: string
        | InvalidDigest of field: string * observed: string
        | InvalidIdentity of field: string * observed: string
        | DuplicateCatalogUnit of unitId: string
        | NextUnitMissing
        | MultipleNextUnits of unitIds: string list
        | PrerequisiteNotAccepted of unitId: string * prerequisite: string
        | RoadmapIdentityMismatch of field: string
        | DuplicateGate of gate: string
        | EvidenceObligationMissing of obligation: string
        | RegistrationInvalid of registrationId: string * reason: string
        | RegistrationDuplicate of registrationId: string
        | QualificationMismatch of reason: string
        | LifecycleInvalid of reason: string
        | SddObservationInvalid of stage: string * reason: string
        | RevisionIdentityCollapse of left: string * right: string
        | RevisionRelationInvalid of reason: string
        | ReviewEvidenceMissing
        | BoundedPatchInvalid of reason: string
        | AcceptanceBundleInvalid of reason: string

    let private sha = Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)
    let private revision = Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)
    let private unitId = Regex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)
    let private issue = Regex("^https://github.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/[1-9][0-9]*$", RegexOptions.CultureInvariant)
    let private issueRef = Regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+#[1-9][0-9]*$", RegexOptions.CultureInvariant)
    let private token = Regex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)
    let private acceptanceLifecyclePhases =
        [ "intake"; "claim"; "sdd-analyze"; "implementation"; "sdd-verify"; "sdd-ship"
          "qualification"; "review"; "host-acceptance"; "merge"; "acceptance" ]
    let private digest bytes = CanonicalJson.sha256 bytes
    let private utf8 (value: string) = Encoding.UTF8.GetBytes value
    let private canonical bytes = CanonicalJson.canonicalize bytes |> Result.defaultWith invalidOp
    let private stateName = function Accepted -> "accepted" | Unchecked -> "unchecked"
    let private dispositionName = function Some Intake.Create -> "create" | Some Intake.Reuse -> "reuse" | None -> ""

    let private draftDto (draft: Intake.Draft) =
        {| schema = draft.Schema; id = draft.Id; owner = draft.Owner; repository = draft.Repository
           title = draft.Title; observed = draft.Observed; rootCause = draft.RootCause
           acceptance = draft.Acceptance; verification = draft.Verification; paths = draft.Paths
           ``class`` = draft.Class; status = draft.Status; disposition = dispositionName draft.Disposition
           phase = draft.Phase; severity = draft.Severity; blockedBy = draft.BlockedBy
           blockedOn = draft.BlockedOn; backlogReason = draft.BacklogReason
           judgementQuestion = draft.JudgementQuestion |}

    let private rowDto (row: CatalogRow) =
        {| unitId = row.UnitId; title = row.Title; state = stateName row.State
           prerequisite = row.Prerequisite; gates = row.Gates; evidenceObligations = row.EvidenceObligations
           contractSha256 = row.ContractSha256 |}

    let private canonicalPlanPayload (plan: PreparationPlan) includeDigest =
        let registrations =
            plan.Registrations
            |> List.map (fun value -> {| id = value.Id; kind = value.Kind; draft = draftDto value.Draft |})
        JsonSerializer.SerializeToUtf8Bytes
            {| schema = plan.Schema; unit = rowDto plan.Unit; acceptedPrerequisite = plan.AcceptedPrerequisite
               authority = {| roadmapRevision = plan.Authority.RoadmapRevision; roadmapDigest = plan.Authority.RoadmapDigest; catalogDigest = plan.Authority.CatalogDigest; issue = plan.Authority.Issue |}
               sddWorkId = plan.SddWorkId; registrations = registrations; gateRegistrations = plan.GateRegistrations
               evidenceObligations = plan.EvidenceObligations
               digest = if includeDigest then plan.Digest else "" |}
        |> canonical

    let canonicalPlan plan = canonicalPlanPayload plan true + "\n"

    let canonicalIntakeDraft (registration: Registration) =
        let draft = registration.Draft
        let node = JsonObject()
        node["schema"] <- JsonValue.Create draft.Schema
        node["id"] <- JsonValue.Create draft.Id
        node["owner"] <- JsonValue.Create draft.Owner
        node["repository"] <- JsonValue.Create draft.Repository
        node["title"] <- JsonValue.Create draft.Title
        node["observed"] <- JsonValue.Create draft.Observed
        node["rootCause"] <- JsonValue.Create draft.RootCause
        node["acceptance"] <- JsonValue.Create draft.Acceptance
        node["verification"] <- JsonValue.Create draft.Verification
        let paths = JsonArray()
        draft.Paths |> List.iter (JsonValue.Create >> paths.Add)
        node["paths"] <- paths
        node["class"] <- JsonValue.Create draft.Class
        node["status"] <- JsonValue.Create draft.Status
        node["disposition"] <- JsonValue.Create(dispositionName draft.Disposition)
        for name, value in
            [ "phase", draft.Phase; "severity", draft.Severity; "blockedBy", draft.BlockedBy
              "blockedOn", draft.BlockedOn; "backlogReason", draft.BacklogReason
              "judgementQuestion", draft.JudgementQuestion ] do
            value |> Option.iter (fun text -> node[name] <- JsonValue.Create text)
        canonical (JsonSerializer.SerializeToUtf8Bytes node) + "\n"

    let private duplicateValues (values: string list) =
        values |> List.countBy id |> List.choose (fun (value, count) -> if count > 1 then Some value else None)

    let private applicationPayload (application: PreparationApplication) includeDigest =
        let registrations =
            application.Registrations
            |> List.map (fun value ->
                {| id = value.Id; kind = value.Kind; draftSha256 = value.DraftSha256
                   issue = value.Issue; issueUrl = value.IssueUrl |})
        JsonSerializer.SerializeToUtf8Bytes
            {| schema = application.Schema; unitId = application.UnitId
               planDigest = application.PlanDigest; registrations = registrations
               digest = if includeDigest then application.Digest else "" |}
        |> canonical

    let canonicalPreparationApplication application = applicationPayload application true + "\n"

    let sealPreparationApplication (plan: PreparationPlan) (registrations: AppliedRegistration list) =
        let findings = ResizeArray<Finding>()
        if registrations.Length <> plan.Registrations.Length then
            findings.Add(RegistrationInvalid(plan.Unit.UnitId, "application registration count differs from the plan"))
        for duplicate in registrations |> List.map _.Id |> duplicateValues do
            findings.Add(RegistrationDuplicate duplicate)
        for expected in plan.Registrations do
            match registrations |> List.filter (fun observed -> observed.Id = expected.Id) with
            | [ observed ] ->
                if observed.Kind <> expected.Kind then
                    findings.Add(RegistrationInvalid(expected.Id, "application kind differs from the plan"))
                if observed.DraftSha256 <> IntakeReceipt.digest expected.Draft then
                    findings.Add(RegistrationInvalid(expected.Id, "application draft digest differs from the plan"))
                if not (issueRef.IsMatch observed.Issue) || not (issue.IsMatch observed.IssueUrl) then
                    findings.Add(RegistrationInvalid(expected.Id, "application issue identity is malformed"))
                else
                    let marker = observed.Issue.LastIndexOf('#')
                    let repository = observed.Issue.Substring(0, marker)
                    let number = observed.Issue.Substring(marker + 1)
                    if observed.IssueUrl <> $"https://github.com/%s{repository}/issues/%s{number}" then
                        findings.Add(RegistrationInvalid(expected.Id, "application issue URL differs from its canonical issue"))
            | [] -> findings.Add(RegistrationInvalid(expected.Id, "application registration is missing"))
            | _ -> findings.Add(RegistrationDuplicate expected.Id)
        if findings.Count > 0 then Error(List.ofSeq findings)
        else
            let unsigned =
                { Schema = PreparationApplicationSchema; UnitId = plan.Unit.UnitId
                  PlanDigest = plan.Digest; Registrations = registrations; Digest = "" }
            Ok { unsigned with Digest = digest (utf8 (applicationPayload unsigned false)) }

    let canonicalRevisionCommand repository candidate merge =
        String.concat "\n"
            [ $"git -C %s{repository} rev-parse %s{candidate}^{{tree}}"
              $"git -C %s{repository} rev-parse %s{merge}^{{tree}}"
              $"git -C %s{repository} diff --quiet %s{candidate}^{{tree}} %s{merge}^{{tree}}" ]
        + "\n"

    let private revisionBindingPayload (binding: RevisionBinding) includeDigest =
        JsonSerializer.SerializeToUtf8Bytes
            {| schema = binding.Schema; repository = binding.Repository
               candidate = binding.Candidate; merge = binding.Merge
               candidateTree = binding.CandidateTree; mergeTree = binding.MergeTree
               commandSha256 = binding.CommandSha256; exitCode = binding.ExitCode
               digest = if includeDigest then binding.Digest else "" |}
        |> canonical

    let sealRevisionBinding repository candidate merge candidateTree mergeTree exitCode =
        let commandSha256 = digest (utf8 (canonicalRevisionCommand repository candidate merge))
        let unsigned =
            { Schema = RevisionBindingSchema; Repository = repository; Candidate = candidate; Merge = merge
              CandidateTree = candidateTree; MergeTree = mergeTree; CommandSha256 = commandSha256
              ExitCode = exitCode; Digest = "" }
        { unsigned with Digest = digest (utf8 (revisionBindingPayload unsigned false)) }

    let canonicalRevisionBinding binding = revisionBindingPayload binding true + "\n"

    let private registrationDraft (owner: string) (repository: string) (paths: string list) (unit: CatalogRow) (kind: string) (id: string) (title: string) : Intake.Draft =
        { Schema = Intake.Schema
          Id = id
          Owner = owner
          Repository = repository
          Title = title
          Observed = $"Roadmap unit %s{unit.UnitId} requires deterministic %s{kind} registration."
          RootCause = "The canonical roadmap row is the sole registration authority."
          Acceptance = $"Registration identity matches roadmap unit %s{unit.UnitId} and replays through staged intake."
          Verification = "Inspect the compiler plan digest and authoritative intake receipt readback."
          Paths = paths
          Class = "hardening"
          Status = "Backlog"
          Disposition = Some Intake.Create
          Phase = Some "P5 Versioning"
          Severity = Some "High"
          BlockedBy = None
          BlockedOn = None
          BacklogReason = Some "not-yet-actionable"
          JudgementQuestion = None }

    let inspectPreparation (input: PreparationInput) =
        let findings = ResizeArray<Finding>()
        if input.Schema <> PreparationInputSchema then findings.Add(InvalidSchema(PreparationInputSchema, input.Schema))
        if not (revision.IsMatch input.RoadmapRevision) then findings.Add(InvalidIdentity("roadmapRevision", input.RoadmapRevision))
        if not (Regex.IsMatch(input.RoadmapSourceDigest, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)) then findings.Add(InvalidDigest("roadmapSourceDigest", input.RoadmapSourceDigest))
        if not (Regex.IsMatch(input.CatalogSourceDigest, "^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)) then findings.Add(InvalidDigest("catalogSourceDigest", input.CatalogSourceDigest))
        if not (issue.IsMatch input.AuthorityIssue) then findings.Add(InvalidIdentity("authorityIssue", input.AuthorityIssue))
        let authorityNumber = input.AuthorityIssue.Split('/') |> Array.tryLast |> Option.defaultValue ""
        if not (Regex.IsMatch(input.SddWorkId, "^[1-9][0-9]*-[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant))
           || not (input.SddWorkId.StartsWith(authorityNumber + "-", StringComparison.Ordinal)) then
            findings.Add(InvalidIdentity("sddWorkId", input.SddWorkId))
        for duplicate in input.Catalog |> List.map _.UnitId |> duplicateValues do findings.Add(DuplicateCatalogUnit duplicate)
        let acceptedIds = input.Catalog |> List.choose (fun row -> if row.State = Accepted then Some row.UnitId else None) |> Set.ofList
        let eligible =
            input.Catalog
            |> List.filter (fun row ->
                row.State = Unchecked
                && (row.Prerequisite |> Option.exists acceptedIds.Contains))
        match eligible with
        | [] -> findings.Add NextUnitMissing
        | _ :: _ :: _ -> findings.Add(MultipleNextUnits(eligible |> List.map _.UnitId))
        | _ -> ()
        let firstUnchecked = input.Catalog |> List.tryFind (fun row -> row.State = Unchecked)
        for row in input.Catalog do
            if not (unitId.IsMatch row.UnitId) || String.IsNullOrWhiteSpace row.Title then findings.Add(InvalidIdentity("catalogRow", row.UnitId))
            if not (sha.IsMatch row.ContractSha256) then findings.Add(InvalidDigest($"%s{row.UnitId}.contractSha256", row.ContractSha256))
            for gate in duplicateValues row.Gates do findings.Add(DuplicateGate gate)
            match row.State, row.Prerequisite with
            | Unchecked, Some prerequisite when firstUnchecked = Some row && not (acceptedIds.Contains prerequisite) -> findings.Add(PrerequisiteNotAccepted(row.UnitId, prerequisite))
            | Unchecked, None when firstUnchecked = Some row -> findings.Add(PrerequisiteNotAccepted(row.UnitId, "<missing>"))
            | _ -> ()
        if findings.Count > 0 then Error(List.ofSeq findings) else
        let selected = eligible.Head
        let prerequisite = selected.Prerequisite.Value
        if input.RoadmapRow.UnitId <> selected.UnitId then findings.Add(RoadmapIdentityMismatch "unitId")
        if input.RoadmapRow.Title <> selected.Title then findings.Add(RoadmapIdentityMismatch "title")
        if input.RoadmapRow.Prerequisite <> selected.Prerequisite then findings.Add(RoadmapIdentityMismatch "prerequisite")
        if input.RoadmapRow.Gates <> selected.Gates then findings.Add(RoadmapIdentityMismatch "gates")
        let required = [ "sdd:analyze"; "sdd:verify"; "sdd:ship"; "qualification"; "lifecycle"; "review" ]
        for obligation in required do if not (List.contains obligation selected.EvidenceObligations) then findings.Add(EvidenceObligationMissing obligation)
        if findings.Count > 0 then Error(List.ofSeq findings) else
        let slug (value: string) = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-')
        let registrationId = $"roadmap-unit-%s{slug selected.UnitId}"
        let registrations =
            [ { Id = registrationId; Kind = "unit"
                Draft = registrationDraft input.RegistrationOwner input.RegistrationRepository input.RegistrationPaths selected "unit" registrationId $"[roadmap] %s{selected.UnitId} — %s{selected.Title}" } ]
        let gateRegistrations =
            selected.Gates
            |> List.map (fun gate -> $"roadmap-gate-%s{slug selected.UnitId}-%s{slug gate}")
        for duplicate in registrations |> List.map _.Id |> duplicateValues do findings.Add(RegistrationDuplicate duplicate)
        for registration in registrations do
            match Intake.validate registration.Draft with
            | Error errors -> errors |> List.iter (fun value -> findings.Add(RegistrationInvalid(registration.Id, $"%s{value.Field} %s{value.Detail}")))
            | Ok _ -> ()
        if findings.Count > 0 then Error(List.ofSeq findings) else
        let draft =
            { Schema = PreparationPlanSchema; Unit = selected; AcceptedPrerequisite = prerequisite
              Authority = { RoadmapRevision = input.RoadmapRevision; RoadmapDigest = input.RoadmapSourceDigest; CatalogDigest = input.CatalogSourceDigest; Issue = input.AuthorityIssue }
              SddWorkId = input.SddWorkId; Registrations = registrations; GateRegistrations = gateRegistrations
              EvidenceObligations = selected.EvidenceObligations; Digest = "" }
        let planDigest = canonicalPlanPayload draft false |> utf8 |> digest
        Ok { draft with Digest = planDigest }

    let private marker (unit: string) = $"<!-- fsgg:roadmap-registration/%s{unit} -->"
    let private endMarker (unit: string) = $"<!-- /fsgg:roadmap-registration/%s{unit} -->"
    let private bounds (text: string) (unit: string) =
        let first, last = marker unit, endMarker unit
        let starts = Regex.Matches(text, Regex.Escape first)
        let ends = Regex.Matches(text, Regex.Escape last)
        if starts.Count <> 1 || ends.Count <> 1 || ends[0].Index <= starts[0].Index then Error [ BoundedPatchInvalid "registration marker authority is missing or ambiguous" ]
        else Ok(starts[0].Index, ends[0].Index + last.Length)

    let private renderBlock (plan: PreparationPlan) =
        let registrationLines = plan.Registrations |> List.map (fun value -> $"- `%s{value.Kind}` `%s{value.Id}` — staged intake `%s{value.Draft.Id}`")
        let gateLines = plan.GateRegistrations |> List.map (fun value -> $"- `gate` `%s{value}` — owned by `%s{plan.Registrations.Head.Id}`")
        String.concat "\n" ([ marker plan.Unit.UnitId; $"Authority: `%s{plan.Authority.RoadmapDigest}` via %s{plan.Authority.Issue}"; $"Unit: `%s{plan.Unit.UnitId}` — %s{plan.Unit.Title}"; $"Prerequisite: `%s{plan.AcceptedPrerequisite}`"; "Owning registration:" ] @ registrationLines @ [ "Gate registrations:" ] @ gateLines @ [ "Evidence obligations: " + String.concat ", " plan.EvidenceObligations; $"Plan digest: `%s{plan.Digest}`"; endMarker plan.Unit.UnitId ])

    let renderPreparation (source: byte array) (plan: PreparationPlan) =
        let text = Encoding.UTF8.GetString source
        match bounds text plan.Unit.UnitId with
        | Error errors -> Error errors
        | Ok(first, last) -> Ok(text.Substring(0, first) + renderBlock plan + text.Substring(last))

    let verifyPreparation (source: byte array) (candidate: byte array) (plan: PreparationPlan) =
        let sourceText, candidateText = Encoding.UTF8.GetString source, Encoding.UTF8.GetString candidate
        match bounds sourceText plan.Unit.UnitId, bounds candidateText plan.Unit.UnitId with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok(sf, sl), Ok(cf, cl) ->
            if sourceText.Substring(0, sf) <> candidateText.Substring(0, cf) || sourceText.Substring(sl) <> candidateText.Substring(cl) then Error [ BoundedPatchInvalid "content outside the registration marker was modified" ]
            elif candidateText.Substring(cf, cl - cf) <> renderBlock plan then Error [ BoundedPatchInvalid "bounded registration block differs from the canonical plan" ]
            else Ok ()

    let private strict (label: string) (expected: string list) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then raise (FormatException($"%s{label} must be an object"))
        let observed = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let expected = Set.ofList expected
        let missing, extra = Set.difference expected observed, Set.difference observed expected
        let missingText, extraText = String.concat "," (Set.toList missing), String.concat "," (Set.toList extra)
        if not missing.IsEmpty then raise (FormatException($"%s{label} is missing fields: %s{missingText}"))
        if not extra.IsEmpty then raise (FormatException($"%s{label} has unknown fields: %s{extraText}"))
    let private prop (name: string) (value: JsonElement) = value.GetProperty name
    let private text (label: string) (name: string) (value: JsonElement) =
        let item = prop name value
        if item.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(item.GetString()) then raise (FormatException($"%s{label}.%s{name} must be a non-empty string"))
        item.GetString()
    let private optionalText (label: string) (name: string) (value: JsonElement) =
        let item = prop name value
        match item.ValueKind with JsonValueKind.Null -> None | JsonValueKind.String when not (String.IsNullOrWhiteSpace(item.GetString())) -> Some(item.GetString()) | _ -> raise (FormatException($"%s{label}.%s{name} must be null or a non-empty string"))
    let private strings (label: string) (name: string) (value: JsonElement) =
        let item = prop name value
        if item.ValueKind <> JsonValueKind.Array then raise (FormatException($"%s{label}.%s{name} must be an array"))
        item.EnumerateArray()
        |> Seq.map (fun entry ->
            if entry.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(entry.GetString()) then
                raise (FormatException($"%s{label}.%s{name} contains an invalid string"))
            entry.GetString())
        |> List.ofSeq
    let private parseState (value: string) = match value with "accepted" -> Accepted | "unchecked" -> Unchecked | value -> raise (FormatException($"unknown unit state '%s{value}'"))
    let private parseRow (label: string) (value: JsonElement) : CatalogRow =
        strict label [ "unitId"; "title"; "state"; "prerequisite"; "gates"; "evidenceObligations"; "contractSha256" ] value
        { UnitId = text label "unitId" value; Title = text label "title" value; State = text label "state" value |> parseState
          Prerequisite = optionalText label "prerequisite" value; Gates = strings label "gates" value
          EvidenceObligations = strings label "evidenceObligations" value
          ContractSha256 = text label "contractSha256" value }
    let private parseRoadmapRow (label: string) (value: JsonElement) : RoadmapRow =
        strict label [ "unitId"; "title"; "prerequisite"; "gates" ] value
        { UnitId = text label "unitId" value; Title = text label "title" value
          Prerequisite = optionalText label "prerequisite" value; Gates = strings label "gates" value }
    let private array (name: string) (value: JsonElement) =
        let item = prop name value
        if item.ValueKind <> JsonValueKind.Array then raise (FormatException($"%s{name} must be an array"))
        item.EnumerateArray() |> List.ofSeq

    let parsePreparationApplication (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "preparationApplication" [ "schema"; "unitId"; "planDigest"; "registrations"; "digest" ] root
            let registrations =
                array "registrations" root
                |> List.mapi (fun index item ->
                    strict $"registrations[%d{index}]" [ "id"; "kind"; "draftSha256"; "issue"; "issueUrl" ] item
                    { Id = text "registration" "id" item; Kind = text "registration" "kind" item
                      DraftSha256 = text "registration" "draftSha256" item
                      Issue = text "registration" "issue" item; IssueUrl = text "registration" "issueUrl" item })
            let observed =
                { Schema = text "preparationApplication" "schema" root
                  UnitId = text "preparationApplication" "unitId" root
                  PlanDigest = text "preparationApplication" "planDigest" root
                  Registrations = registrations; Digest = text "preparationApplication" "digest" root }
            let errors = ResizeArray<string>()
            if observed.Schema <> PreparationApplicationSchema then errors.Add($"schema must be '%s{PreparationApplicationSchema}'")
            if not (unitId.IsMatch observed.UnitId) then errors.Add("unitId is invalid")
            if not (sha.IsMatch observed.PlanDigest) then errors.Add("planDigest is invalid")
            if not (sha.IsMatch observed.Digest) then errors.Add("digest is invalid")
            if digest (utf8 (applicationPayload observed false)) <> observed.Digest then errors.Add("digest does not match canonical application content")
            for registration in registrations do
                if not (sha.IsMatch registration.DraftSha256) then errors.Add($"registration %s{registration.Id} draftSha256 is invalid")
            if errors.Count = 0 then Ok observed else Error(List.ofSeq errors)
        with error -> Error [ $"invalid preparation application: %s{error.Message}" ]

    let parseRevisionBinding (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "revisionBinding" [ "schema"; "repository"; "candidate"; "merge"; "candidateTree"; "mergeTree"; "commandSha256"; "exitCode"; "digest" ] root
            let binding =
                { Schema = text "revisionBinding" "schema" root
                  Repository = text "revisionBinding" "repository" root
                  Candidate = text "revisionBinding" "candidate" root; Merge = text "revisionBinding" "merge" root
                  CandidateTree = text "revisionBinding" "candidateTree" root; MergeTree = text "revisionBinding" "mergeTree" root
                  CommandSha256 = text "revisionBinding" "commandSha256" root
                  ExitCode = (prop "exitCode" root).GetInt32(); Digest = text "revisionBinding" "digest" root }
            let errors = ResizeArray<string>()
            if binding.Schema <> RevisionBindingSchema then errors.Add($"schema must be '%s{RevisionBindingSchema}'")
            for name, value in [ "candidate", binding.Candidate; "merge", binding.Merge; "candidateTree", binding.CandidateTree; "mergeTree", binding.MergeTree ] do
                if not (revision.IsMatch value) then errors.Add($"%s{name} must be an exact 40-hex commit SHA")
            if String.IsNullOrWhiteSpace binding.Repository then errors.Add("repository must not be blank")
            let expectedCommand = digest (utf8 (canonicalRevisionCommand binding.Repository binding.Candidate binding.Merge))
            if binding.CommandSha256 <> expectedCommand then errors.Add("commandSha256 does not bind the canonical Git observation commands")
            if not (sha.IsMatch binding.Digest) || digest (utf8 (revisionBindingPayload binding false)) <> binding.Digest then errors.Add("digest does not match canonical revision-binding content")
            if errors.Count = 0 then Ok binding else Error(List.ofSeq errors)
        with error -> Error [ $"invalid revision binding: %s{error.Message}" ]

    let parsePreparationInput (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "preparationInput" [ "schema"; "roadmapRevision"; "roadmapSourceDigest"; "catalogSourceDigest"; "catalog"; "roadmapRow"; "authorityIssue"; "sddWorkId"; "registrationOwner"; "registrationRepository"; "registrationPaths" ] root
            Ok { Schema = text "preparationInput" "schema" root; RoadmapRevision = text "preparationInput" "roadmapRevision" root; RoadmapSourceDigest = text "preparationInput" "roadmapSourceDigest" root; CatalogSourceDigest = text "preparationInput" "catalogSourceDigest" root
                 Catalog = array "catalog" root |> List.mapi (fun index row -> parseRow $"catalog[%d{index}]" row)
                 RoadmapRow = parseRoadmapRow "roadmapRow" (prop "roadmapRow" root)
                 AuthorityIssue = text "preparationInput" "authorityIssue" root; SddWorkId = text "preparationInput" "sddWorkId" root; RegistrationOwner = text "preparationInput" "registrationOwner" root
                 RegistrationRepository = text "preparationInput" "registrationRepository" root; RegistrationPaths = strings "preparationInput" "registrationPaths" root }
        with error -> Error [ $"invalid roadmap preparation input: %s{error.Message}" ]

    let parsePreparationRequest (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "preparationRequest" [ "schema"; "roadmapRevision"; "authorityIssue"; "sddWorkId"; "registrationOwner"; "registrationRepository"; "registrationPaths" ] root
            Ok { Schema = text "preparationRequest" "schema" root
                 RoadmapRevision = text "preparationRequest" "roadmapRevision" root
                 AuthorityIssue = text "preparationRequest" "authorityIssue" root
                 SddWorkId = text "preparationRequest" "sddWorkId" root
                 RegistrationOwner = text "preparationRequest" "registrationOwner" root
                 RegistrationRepository = text "preparationRequest" "registrationRepository" root
                 RegistrationPaths = strings "preparationRequest" "registrationPaths" root }
        with error -> Error [ $"invalid roadmap preparation request: %s{error.Message}" ]

    let compilePreparation (roadmapBytes: byte array) (catalogBytes: byte array) (request: PreparationRequest) =
        let findings = ResizeArray<Finding>()
        let roadmapDigest = digest roadmapBytes
        let roadmapText = Encoding.UTF8.GetString roadmapBytes
        let roadmapRows =
            Regex.Matches(roadmapText, @"(?m)^- \[([ xX])\] \*\*(GS2-[0-9]+\.[0-9]+) — (.+?)\.\*\*")
            |> Seq.cast<Match>
            |> Seq.map (fun matched -> matched.Groups[2].Value, (matched.Groups[1].Value <> " ", matched.Groups[3].Value))
            |> List.ofSeq
        for duplicate in roadmapRows |> List.map fst |> duplicateValues do findings.Add(DuplicateCatalogUnit duplicate)
        let roadmapById = roadmapRows |> Map.ofList
        let catalogRows = ResizeArray<CatalogRow>()
        let canonicalBytesOmitting omittedRootMember (element: JsonElement) =
            use stream = new MemoryStream()
            use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false))
            let rec write isRoot (value: JsonElement) =
                match value.ValueKind with
                | JsonValueKind.Object ->
                    writer.WriteStartObject()
                    value.EnumerateObject()
                    |> Seq.filter (fun memberValue -> not (isRoot && memberValue.Name = omittedRootMember))
                    |> Seq.sortBy _.Name
                    |> Seq.iter (fun memberValue -> writer.WritePropertyName(memberValue.Name); write false memberValue.Value)
                    writer.WriteEndObject()
                | JsonValueKind.Array -> writer.WriteStartArray(); value.EnumerateArray() |> Seq.iter (write false); writer.WriteEndArray()
                | JsonValueKind.String -> writer.WriteStringValue(value.GetString())
                | JsonValueKind.Number -> writer.WriteRawValue(value.GetRawText(), true)
                | JsonValueKind.True -> writer.WriteBooleanValue true
                | JsonValueKind.False -> writer.WriteBooleanValue false
                | JsonValueKind.Null -> writer.WriteNullValue()
                | _ -> invalidOp "unsupported JSON token"
            write true element
            writer.Flush()
            stream.ToArray()
        try
            use document = JsonDocument.Parse(ReadOnlyMemory catalogBytes)
            let root = document.RootElement
            strict "catalog" [ "schema"; "roadmap"; "units" ] root
            if text "catalog" "schema" root <> "fsgg.coordination.roadmap-index/1" then
                findings.Add(InvalidSchema("fsgg.coordination.roadmap-index/1", text "catalog" "schema" root))
            let authority = prop "roadmap" root
            strict "catalog.roadmap" [ "repository"; "revision"; "path"; "sha256" ] authority
            if text "catalog.roadmap" "repository" authority <> "FS-GG/.github" then findings.Add(RoadmapIdentityMismatch "repository")
            if text "catalog.roadmap" "path" authority <> "docs/github-substrate-v2-roadmap.md" then findings.Add(RoadmapIdentityMismatch "path")
            let pinnedRevision = text "catalog.roadmap" "revision" authority
            if not (revision.IsMatch pinnedRevision) then findings.Add(InvalidIdentity("catalog.roadmap.revision", pinnedRevision))
            elif pinnedRevision <> request.RoadmapRevision then findings.Add(RoadmapIdentityMismatch "catalog.roadmap.revision")
            let pinnedDigest = text "catalog.roadmap" "sha256" authority
            if pinnedDigest <> roadmapDigest then findings.Add(InvalidDigest("catalog.roadmap.sha256", pinnedDigest))
            for index, unit in array "units" root |> List.indexed do
                let label = $"catalog.units[%d{index}]"
                let allowed =
                    [ "id"; "title"; "owner"; "prerequisites"; "permissionCeiling"; "exitGate"; "qGates"
                      "gateCommands"; "gateContracts"; "contractSha256" ]
                if unit.ValueKind <> JsonValueKind.Object then raise (FormatException($"%s{label} must be an object"))
                let observed = unit.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
                let required = Set.ofList (allowed |> List.except [ "gateContracts" ])
                let unknown = Set.difference observed (Set.ofList allowed)
                let unknownText = String.concat "," (Set.toList unknown)
                if not unknown.IsEmpty then raise (FormatException($"%s{label} has unknown fields: %s{unknownText}"))
                let missing = Set.difference required observed
                let missingText = String.concat "," (Set.toList missing)
                if not missing.IsEmpty then raise (FormatException($"%s{label} is missing fields: %s{missingText}"))
                let id = text label "id" unit
                let title = text label "title" unit
                let prerequisites = strings label "prerequisites" unit
                let qGates = strings label "qGates" unit
                let gateCommands = strings label "gateCommands" unit
                let contractDigest = text label "contractSha256" unit
                if not (sha.IsMatch contractDigest) then findings.Add(InvalidDigest($"%s{id}.contractSha256", contractDigest))
                let calculatedContractDigest = canonicalBytesOmitting "contractSha256" unit |> digest
                if calculatedContractDigest <> contractDigest then findings.Add(InvalidDigest($"%s{id}.contractSha256", contractDigest))
                match Map.tryFind id roadmapById with
                | None -> findings.Add(RoadmapIdentityMismatch($"missing roadmap row %s{id}"))
                | Some(_, roadmapTitle) when roadmapTitle <> title -> findings.Add(RoadmapIdentityMismatch($"title %s{id}"))
                | Some(accepted, _) ->
                    let prerequisite = prerequisites |> List.tryLast
                    catalogRows.Add
                        { UnitId = id; Title = title; State = if accepted then Accepted else Unchecked
                          Prerequisite = prerequisite; Gates = qGates @ gateCommands
                          EvidenceObligations = [ "sdd:analyze"; "sdd:verify"; "sdd:ship"; "qualification"; "lifecycle"; "review" ]
                          ContractSha256 = contractDigest }
        with error -> findings.Add(RoadmapIdentityMismatch($"catalog parse: %s{error.Message}"))
        let rows = List.ofSeq catalogRows
        let firstUncheckedRoadmap = roadmapRows |> List.tryFind (fun (_, (accepted, _)) -> not accepted) |> Option.map fst
        let firstUnchecked = rows |> List.tryFindIndex (fun row -> row.State = Unchecked)
        match firstUnchecked with
        | Some index when rows |> List.skip (index + 1) |> List.exists (fun row -> row.State = Accepted) ->
            findings.Add(RoadmapIdentityMismatch "accepted unit occurs after the unchecked frontier")
        | _ -> ()
        if findings.Count > 0 then Error(List.ofSeq findings)
        else
            match firstUnchecked with
            | None -> Error [ NextUnitMissing ]
            | Some index when firstUncheckedRoadmap <> Some rows[index].UnitId ->
                Error [ RoadmapIdentityMismatch "catalog omits or reorders the canonical first unchecked roadmap row" ]
            | Some index when
                (rows |> List.take (index + 1) |> List.map _.UnitId)
                <> (roadmapRows |> List.map fst |> List.filter (Set.ofList (rows |> List.take (index + 1) |> List.map _.UnitId)).Contains) ->
                Error [ RoadmapIdentityMismatch "catalog prefix does not match canonical roadmap order through the first unchecked row" ]
            | Some index ->
                let selected = rows[index]
                let accepted = rows |> List.take index |> List.map _.UnitId |> Set.ofList
                match selected.Prerequisite with
                | None -> Error [ PrerequisiteNotAccepted(selected.UnitId, "<missing>") ]
                | Some prerequisite when not (accepted.Contains prerequisite) -> Error [ PrerequisiteNotAccepted(selected.UnitId, prerequisite) ]
                | Some prerequisite when index = 0 || rows[index - 1].UnitId <> prerequisite ->
                    Error [ RoadmapIdentityMismatch "selected unit prerequisite is not the immediate preceding catalog row" ]
                | Some _ ->
                    inspectPreparation
                        { Schema = request.Schema
                          RoadmapRevision = request.RoadmapRevision
                          RoadmapSourceDigest = "sha256:" + roadmapDigest
                          CatalogSourceDigest = "sha256:" + digest catalogBytes
                          Catalog = rows |> List.take (index + 1)
                          RoadmapRow = { UnitId = selected.UnitId; Title = selected.Title; Prerequisite = selected.Prerequisite; Gates = selected.Gates }
                          AuthorityIssue = request.AuthorityIssue
                          SddWorkId = request.SddWorkId
                          RegistrationOwner = request.RegistrationOwner
                          RegistrationRepository = request.RegistrationRepository
                          RegistrationPaths = request.RegistrationPaths }

    let private parseDraft (label: string) (value: JsonElement) : Intake.Draft =
        strict label [ "schema"; "id"; "owner"; "repository"; "title"; "observed"; "rootCause"; "acceptance"; "verification"; "paths"; "class"; "status"; "disposition"; "phase"; "severity"; "blockedBy"; "blockedOn"; "backlogReason"; "judgementQuestion" ] value
        let disposition = match text label "disposition" value with "create" -> Some Intake.Create | "reuse" -> Some Intake.Reuse | other -> raise (FormatException($"unknown disposition '%s{other}'"))
        { Schema = text label "schema" value; Id = text label "id" value; Owner = text label "owner" value
          Repository = text label "repository" value; Title = text label "title" value; Observed = text label "observed" value
          RootCause = text label "rootCause" value; Acceptance = text label "acceptance" value; Verification = text label "verification" value
          Paths = strings label "paths" value; Class = text label "class" value; Status = text label "status" value; Disposition = disposition
          Phase = optionalText label "phase" value; Severity = optionalText label "severity" value; BlockedBy = optionalText label "blockedBy" value
          BlockedOn = optionalText label "blockedOn" value; BacklogReason = optionalText label "backlogReason" value; JudgementQuestion = optionalText label "judgementQuestion" value }

    let parsePlan (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "plan" [ "schema"; "unit"; "acceptedPrerequisite"; "authority"; "sddWorkId"; "registrations"; "gateRegistrations"; "evidenceObligations"; "digest" ] root
            let authority = prop "authority" root
            strict "authority" [ "roadmapRevision"; "roadmapDigest"; "catalogDigest"; "issue" ] authority
            let registrations = array "registrations" root |> List.mapi (fun index item -> strict $"registrations[%d{index}]" [ "id"; "kind"; "draft" ] item; { Id = text "registration" "id" item; Kind = text "registration" "kind" item; Draft = parseDraft "registration.draft" (prop "draft" item) })
            let plan =
                { Schema = text "plan" "schema" root; Unit = parseRow "unit" (prop "unit" root); AcceptedPrerequisite = text "plan" "acceptedPrerequisite" root
                  Authority = { RoadmapRevision = text "authority" "roadmapRevision" authority; RoadmapDigest = text "authority" "roadmapDigest" authority; CatalogDigest = text "authority" "catalogDigest" authority; Issue = text "authority" "issue" authority }
                  SddWorkId = text "plan" "sddWorkId" root; Registrations = registrations
                  GateRegistrations = strings "plan" "gateRegistrations" root
                  EvidenceObligations = strings "plan" "evidenceObligations" root; Digest = text "plan" "digest" root }
            let actual = canonicalPlanPayload { plan with Digest = "" } false |> utf8 |> digest
            if plan.Schema <> PreparationPlanSchema then Error [ $"plan schema is not %s{PreparationPlanSchema}" ]
            elif not (sha.IsMatch plan.Digest) || actual <> plan.Digest then Error [ "plan digest does not bind canonical content" ]
            else Ok plan
        with error -> Error [ $"invalid roadmap preparation plan: %s{error.Message}" ]

    let private evidenceEntries (input: AcceptanceInput) =
        [ yield { Name = "preparation-plan"; Sha256 = input.Plan.Digest; Source = input.Plan.Authority.Issue }
          yield { Name = "preparation-application"; Sha256 = input.PreparationApplication.Digest; Source = input.PreparationApplication.UnitId }
          yield { Name = "qualification"; Sha256 = input.Qualification.Digest; Source = input.Qualification.Subject }
          yield { Name = "qualification-evidence"; Sha256 = input.Qualification.EvidenceDigest; Source = input.Qualification.SemanticReviewEvidence }
          yield { Name = "lifecycle"; Sha256 = digest (utf8 input.LifecycleLog); Source = $"%s{input.LifecycleRunId}/%s{input.LifecycleUnitId}" }
          yield { Name = "lifecycle-history"; Sha256 = digest (utf8 input.LifecycleHistoryReport); Source = input.LifecycleRunId }
          for index, receipt in input.LifecycleUsageReceipts |> List.indexed do
              yield { Name = $"lifecycle-usage-%d{index + 1}"; Sha256 = digest (utf8 receipt); Source = input.LifecycleRunId }
          yield { Name = "review"; Sha256 = digest (utf8 input.ReviewReceipt); Source = input.ReviewEvidence }
          yield { Name = "structured-review"; Sha256 = digest (utf8 input.StructuredReviewEvidence); Source = input.StructuredReviewEvidence }
          yield { Name = "revision-binding-implementation"; Sha256 = input.ImplementationBinding.Digest; Source = input.ImplementationBinding.Repository + ":" + input.ImplementationBinding.Candidate + ".." + input.ImplementationBinding.Merge }
          yield { Name = "revision-binding-acceptance"; Sha256 = input.AcceptanceBinding.Digest; Source = input.AcceptanceBinding.Repository + ":" + input.AcceptanceBinding.Candidate + ".." + input.AcceptanceBinding.Merge }
          for observation in input.SddObservations do yield { Name = "sdd-" + observation.Stage; Sha256 = digest (utf8 observation.ArtifactJson); Source = input.SddWorkId } ]

    let private selfDigest (node: JsonObject) =
        let clone = node.DeepClone().AsObject()
        clone.Remove "digest" |> ignore
        canonical (JsonSerializer.SerializeToUtf8Bytes clone) |> utf8 |> digest

    let private buildAcceptance (input: AcceptanceInput) =
        let entries = evidenceEntries input |> List.sortBy _.Name
        let entryNodes = JsonArray()
        for entry in entries do
            let item = JsonObject()
            item["name"] <- JsonValue.Create entry.Name
            item["sha256"] <- JsonValue.Create entry.Sha256
            item["source"] <- JsonValue.Create entry.Source
            entryNodes.Add item
        let identities = input.Identities
        let identityNode = JsonObject()
        identityNode["implementationCandidate"] <- JsonValue.Create identities.ImplementationCandidate
        identityNode["implementationMerge"] <- JsonValue.Create identities.ImplementationMerge
        identityNode["implementationPullRequest"] <- JsonValue.Create identities.ImplementationPullRequest
        identityNode["acceptanceCandidate"] <- JsonValue.Create identities.AcceptanceCandidate
        identityNode["acceptanceMerge"] <- JsonValue.Create identities.AcceptanceMerge
        identityNode["acceptancePullRequest"] <- JsonValue.Create identities.AcceptancePullRequest
        identityNode["protectedMain"] <- JsonValue.Create identities.ProtectedMain
        let obligationNodes = JsonArray()
        input.Plan.EvidenceObligations |> List.iter (JsonValue.Create >> obligationNodes.Add)
        let indexBase = JsonObject()
        indexBase["schema"] <- JsonValue.Create EvidenceIndexSchema
        indexBase["unitId"] <- JsonValue.Create input.Plan.Unit.UnitId
        indexBase["roadmapSourceDigest"] <- JsonValue.Create input.Plan.Authority.RoadmapDigest
        indexBase["catalogSourceDigest"] <- JsonValue.Create input.Plan.Authority.CatalogDigest
        indexBase["unitContractSha256"] <- JsonValue.Create input.Plan.Unit.ContractSha256
        indexBase["identities"] <- identityNode.DeepClone()
        indexBase["obligations"] <- obligationNodes.DeepClone()
        indexBase["entries"] <- entryNodes
        let receiptBase = JsonObject()
        receiptBase["schema"] <- JsonValue.Create "fsgg.coordination.unit-acceptance/1"
        receiptBase["unitId"] <- JsonValue.Create input.Plan.Unit.UnitId
        receiptBase["state"] <- JsonValue.Create "accepted"
        receiptBase["sourceRevision"] <- JsonValue.Create identities.ImplementationCandidate
        receiptBase["acceptedAt"] <- JsonValue.Create input.AcceptedAt
        receiptBase["roadmapSourceDigest"] <- JsonValue.Create input.Plan.Authority.RoadmapDigest
        receiptBase["catalogSourceDigest"] <- JsonValue.Create input.Plan.Authority.CatalogDigest
        receiptBase["unitContractSha256"] <- JsonValue.Create input.Plan.Unit.ContractSha256
        receiptBase["obligations"] <- obligationNodes.DeepClone()
        let artifacts = JsonArray()
        let candidateArtifact = JsonObject()
        candidateArtifact["name"] <- JsonValue.Create("implementation-candidate-" + identities.ImplementationCandidate)
        candidateArtifact["sha256"] <- JsonValue.Create input.Qualification.EvidenceDigest
        artifacts.Add candidateArtifact
        receiptBase["artifacts"] <- artifacts
        receiptBase["identities"] <- identityNode
        let unsignedIndex = canonical (JsonSerializer.SerializeToUtf8Bytes indexBase)
        let unsignedReceipt = canonical (JsonSerializer.SerializeToUtf8Bytes receiptBase)
        let transactionDigest = digest (utf8 (unsignedReceipt + "\n" + unsignedIndex))
        indexBase["transactionDigest"] <- JsonValue.Create transactionDigest
        receiptBase["transactionDigest"] <- JsonValue.Create transactionDigest
        indexBase["digest"] <- JsonValue.Create(String.replicate 64 "0")
        indexBase["digest"] <- JsonValue.Create(selfDigest indexBase)
        receiptBase["evidenceIndexDigest"] <- JsonValue.Create(indexBase["digest"].GetValue<string>())
        receiptBase["digest"] <- JsonValue.Create(String.replicate 64 "0")
        receiptBase["digest"] <- JsonValue.Create(selfDigest receiptBase)
        let receiptJson = canonical (JsonSerializer.SerializeToUtf8Bytes receiptBase) + "\n"
        let indexJson = canonical (JsonSerializer.SerializeToUtf8Bytes indexBase) + "\n"
        let bundle = JsonObject()
        bundle["schema"] <- JsonValue.Create "fsgg.roadmap-unit.acceptance-bundle/1"
        bundle["transactionDigest"] <- JsonValue.Create transactionDigest
        bundle["receipt"] <- receiptBase.DeepClone()
        bundle["evidenceIndex"] <- indexBase.DeepClone()
        bundle["digest"] <- JsonValue.Create(String.replicate 64 "0")
        bundle["digest"] <- JsonValue.Create(selfDigest bundle)
        let bundleJson = canonical (JsonSerializer.SerializeToUtf8Bytes bundle) + "\n"
        { ReceiptJson = receiptJson; EvidenceIndexJson = indexJson; BundleJson = bundleJson; Digest = receiptBase["digest"].GetValue<string>() }

    let inspectAcceptanceCandidate (input: AcceptanceInput) =
        let findings = ResizeArray<Finding>()
        if input.Schema <> AcceptanceInputSchema then findings.Add(InvalidSchema(AcceptanceInputSchema, input.Schema))
        match parsePlan (utf8 (canonicalPlan input.Plan)) with
        | Error errors -> errors |> List.iter (fun reason -> findings.Add(InvalidDigest("plan", reason)))
        | Ok _ -> ()
        match sealPreparationApplication input.Plan input.PreparationApplication.Registrations with
        | Error errors -> errors |> List.iter findings.Add
        | Ok expected when canonicalPreparationApplication expected <> canonicalPreparationApplication input.PreparationApplication ->
            findings.Add(RegistrationInvalid(input.Plan.Unit.UnitId, "preparation application does not match the canonical plan-bound receipt"))
        | Ok _ -> ()
        match Qualification.parseResult (utf8 (Qualification.canonicalResult input.Qualification)) with
        | Error errors -> errors |> List.iter (QualificationMismatch >> findings.Add)
        | Ok _ -> ()
        let appliedUnit =
            input.Plan.Registrations
            |> List.tryFind (fun value -> value.Kind = "unit")
            |> Option.bind (fun unitRegistration ->
                input.PreparationApplication.Registrations
                |> List.tryFind (fun value -> value.Id = unitRegistration.Id))
        match input.Plan.Registrations |> List.tryFind (fun value -> value.Kind = "unit") with
        | None -> findings.Add(RegistrationInvalid(input.Plan.Unit.UnitId, "plan has no unit registration"))
        | Some unitRegistration ->
            match input.PreparationApplication.Registrations |> List.tryFind (fun value -> value.Id = unitRegistration.Id) with
            | Some applied when applied.Issue = input.Qualification.Subject -> ()
            | Some _ -> findings.Add(QualificationMismatch "subject is not the authoritative applied unit registration")
            | None -> findings.Add(RegistrationInvalid(unitRegistration.Id, "applied unit registration is missing"))
        if input.Qualification.SubjectRevision <> input.Identities.ImplementationCandidate then findings.Add(QualificationMismatch "subject revision is not the implementation candidate")
        if input.LifecycleUnitId <> input.Plan.Unit.UnitId then findings.Add(LifecycleInvalid "lifecycle unit id is not the selected roadmap unit")
        if input.LifecycleRunId <> "roadmap-unit-" + input.Plan.Unit.UnitId.ToLowerInvariant() then findings.Add(LifecycleInvalid "lifecycle run id is not the deterministic selected-unit run")
        if input.RequiredLifecyclePhases <> acceptanceLifecyclePhases then findings.Add(LifecycleInvalid "required lifecycle phases are not the compiler-derived intake-through-acceptance sequence")
        if input.ReviewCycleId <> input.Plan.Unit.UnitId then findings.Add(LifecycleInvalid "review cycle id is not the selected roadmap unit")
        let expectedUnitSddWorkId =
            input.PreparationApplication.Registrations
            |> List.tryFind (fun registration -> registration.Kind = "unit")
            |> Option.bind (fun registration ->
                let matched = Regex.Match(registration.Issue, "#([1-9][0-9]*)$", RegexOptions.CultureInvariant)
                if matched.Success then
                    Some($"%s{matched.Groups[1].Value}-roadmap-%s{input.Plan.Unit.UnitId.ToLowerInvariant().Replace('.', '-')}" )
                else None)
        match expectedUnitSddWorkId with
        | None -> findings.Add(SddObservationInvalid("work", "applied unit issue cannot derive the unit SDD work identity"))
        | Some expected when input.SddWorkId <> expected -> findings.Add(SddObservationInvalid("work", $"SDD work id must be derived from the applied unit issue as %s{expected}"))
        | Some _ when input.SddWorkId = input.Plan.SddWorkId -> findings.Add(SddObservationInvalid("work", "unit SDD work id must be distinct from the compiler authority work id"))
        | Some _ -> ()
        if String.IsNullOrWhiteSpace input.Qualification.SemanticReviewEvidence
           || String.IsNullOrWhiteSpace input.ReviewEvidence
           || String.IsNullOrWhiteSpace input.StructuredReviewEvidence then findings.Add ReviewEvidenceMissing
        if input.Qualification.SemanticReviewEvidence <> input.ReviewEvidence then findings.Add(QualificationMismatch "semantic review evidence locator differs from the review authority")
        match CritiqueReceipt.validate input.ReviewCycleId (Some input.Identities.ImplementationCandidate) (utf8 input.ReviewReceipt) with
        | Error errors -> errors |> List.iter (fun error -> findings.Add(LifecycleInvalid("review receipt: " + error)))
        | Ok _ -> ()
        let usageResults =
            input.LifecycleUsageReceipts
            |> List.map (utf8 >> RuntimeUsage.parseCsvReceipt)
        usageResults
        |> List.choose (function Error errors -> Some errors | Ok _ -> None)
        |> List.collect id
        |> List.iter (LifecycleInvalid >> findings.Add)
        let usageReports = usageResults |> List.choose Result.toOption
        match LifecycleTelemetry.parseHistoryCsv input.LifecycleHistoryReport with
        | Error errors -> errors |> List.iter (LifecycleInvalid >> findings.Add)
        | Ok history ->
            match LifecycleTelemetry.validateReconciledWithEvidence input.LifecycleRunId input.LifecycleUnitId true acceptanceLifecyclePhases usageReports history input.LifecycleLog with
            | Error errors -> errors |> List.iter (fun error -> findings.Add(LifecycleInvalid(string error)))
            | Ok _ -> ()
        match appliedUnit with
        | None -> ()
        | Some applied ->
            let rec expectedLifecycleRevision phase =
                match phase with
                | "merge" | "post-merge-obligations" -> input.Identities.ImplementationMerge
                | "acceptance-candidate" -> input.Identities.AcceptanceCandidate
                | "acceptance" | "protected-main-verification" | "receipt-projection" | "cleanup" -> input.Identities.AcceptanceMerge
                | value when value.StartsWith("telemetry-reconciliation-", StringComparison.Ordinal) ->
                    expectedLifecycleRevision (value.Substring("telemetry-reconciliation-".Length))
                | _ -> input.Identities.ImplementationCandidate
            let issueMatch = Regex.Match(applied.Issue, "^([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)#([1-9][0-9]*)$", RegexOptions.CultureInvariant)
            if not issueMatch.Success then findings.Add(LifecycleInvalid "applied unit issue identity is malformed")
            else
                let expectedRepo = issueMatch.Groups[1].Value
                let expectedNumber = Int32.Parse issueMatch.Groups[2].Value
                let expectedUrl = $"https://github.com/%s{expectedRepo}/issues/%d{expectedNumber}"
                input.LifecycleLog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                |> Array.iteri (fun index line ->
                    try
                        use event = JsonDocument.Parse line
                        let root = event.RootElement
                        let item = root.GetProperty "item"
                        let source = root.GetProperty "source"
                        let authority = root.GetProperty "authority"
                        if item.GetProperty("repo").GetString() <> expectedRepo
                           || item.GetProperty("number").GetInt32() <> expectedNumber
                           || item.GetProperty("url").GetString() <> expectedUrl
                           || authority.GetProperty("subject").GetString() <> applied.Issue then
                            findings.Add(LifecycleInvalid($"lifecycle event %d{index + 1} is not bound to the applied unit issue"))
                        let phase = root.GetProperty("phase").GetString()
                        let expectedRevision = expectedLifecycleRevision phase
                        if source.GetProperty("repository").GetString() <> input.ImplementationBinding.Repository
                           || source.GetProperty("revision").GetString() <> expectedRevision then
                            findings.Add(LifecycleInvalid($"lifecycle event %d{index + 1} source revision is invalid for phase %s{phase}"))
                    with error -> findings.Add(LifecycleInvalid($"lifecycle event %d{index + 1} binding: %s{error.Message}")))
        let requiredStages = [ "analyze", "implementationReady"; "verify", "verificationReady"; "ship", "shipReady" ]
        for stage, status in requiredStages do
            match input.SddObservations |> List.filter (fun value -> value.Stage = stage) with
            | [ observation ] ->
                try
                    use artifact = JsonDocument.Parse observation.ArtifactJson
                    let root = artifact.RootElement
                    let observedStage = text "sddArtifact" "stage" root
                    let observedStatus = text "sddArtifact" "status" root
                    let observedWork = text "sddArtifact" "workId" root
                    let schemaVersion = (prop "schemaVersion" root).GetInt32()
                    let viewVersion = text "sddArtifact" "viewVersion" root
                    let generator = text "sddArtifact" "generator" root
                    let readinessNode = prop "readiness" root
                    let readiness =
                        match readinessNode.ValueKind with
                        | JsonValueKind.String -> readinessNode.GetString()
                        | JsonValueKind.Object -> text "sddReadiness" "status" readinessNode
                        | _ -> raise (FormatException("sddArtifact.readiness must be a status string or object"))
                    let sources = prop "sources" root
                    let hasWorkModelSource =
                        sources.ValueKind = JsonValueKind.Array
                        && (sources.EnumerateArray()
                            |> Seq.exists (fun source ->
                                source.ValueKind = JsonValueKind.Object
                                && text "sddSource" "path" source = $"readiness/%s{input.SddWorkId}/work-model.json"))
                    let findingsNode = prop "findings" root
                    let diagnostics = prop "diagnostics" root
                    match CanonicalJson.canonicalize (utf8 observation.ArtifactJson) with
                    | Error reason -> findings.Add(SddObservationInvalid(stage, reason))
                    | Ok _ when schemaVersion = 1 && not (String.IsNullOrWhiteSpace viewVersion)
                                && Regex.IsMatch(generator, "^FS[.]GG[.]SDD[.]Artifacts/[0-9]+[.][0-9]+[.][0-9]+$", RegexOptions.CultureInvariant)
                                && sources.ValueKind = JsonValueKind.Array && sources.GetArrayLength() > 0 && hasWorkModelSource
                                && findingsNode.ValueKind = JsonValueKind.Array && findingsNode.GetArrayLength() = 0
                                && diagnostics.ValueKind = JsonValueKind.Array && diagnostics.GetArrayLength() = 0
                                && observedStage = stage && observedStatus = status && readiness = status
                                && observedWork = input.SddWorkId && observation.SubjectRevision = input.Identities.ImplementationCandidate -> ()
                    | Ok _ -> findings.Add(SddObservationInvalid(stage, $"stage=%s{observedStage} status=%s{observedStatus} work=%s{observedWork} subject=%s{observation.SubjectRevision}"))
                with error -> findings.Add(SddObservationInvalid(stage, error.Message))
            | [] -> findings.Add(SddObservationInvalid(stage, "missing"))
            | _ -> findings.Add(SddObservationInvalid(stage, "duplicate"))
        let named =
            [ "implementationCandidate", input.Identities.ImplementationCandidate
              "implementationMerge", input.Identities.ImplementationMerge
              "acceptanceCandidate", input.Identities.AcceptanceCandidate
              "acceptanceMerge", input.Identities.AcceptanceMerge
              "protectedMain", input.Identities.ProtectedMain ]
        for name, value in named do if not (revision.IsMatch value) then findings.Add(InvalidIdentity(name, value))
        if input.Identities.ImplementationPullRequest < 1 then findings.Add(InvalidIdentity("implementationPullRequest", string input.Identities.ImplementationPullRequest))
        if input.Identities.AcceptancePullRequest < 1 then findings.Add(InvalidIdentity("acceptancePullRequest", string input.Identities.AcceptancePullRequest))
        if input.Identities.ImplementationPullRequest = input.Identities.AcceptancePullRequest then findings.Add(RevisionRelationInvalid "implementation and acceptance pull requests must be distinct")
        let distinct = named |> List.take 4
        for i in 0 .. distinct.Length - 1 do for j in i + 1 .. distinct.Length - 1 do if snd distinct[i] = snd distinct[j] then findings.Add(RevisionIdentityCollapse(fst distinct[i], fst distinct[j]))
        if input.Identities.ProtectedMain <> input.Identities.AcceptanceMerge then findings.Add(RevisionRelationInvalid "protected main must equal the observed acceptance merge")
        let acceptanceRepository =
            input.Plan.Registrations
            |> List.tryFind (fun value -> value.Kind = "unit")
            |> Option.map (fun value -> value.Draft.Owner + "/" + value.Draft.Repository)
            |> Option.defaultValue ""
        let implementationRepository =
            let matched = issue.Match input.Plan.Authority.Issue
            if matched.Success then
                let uri = Uri input.Plan.Authority.Issue
                uri.AbsolutePath.Trim('/').Split('/') |> Array.take 2 |> String.concat "/"
            else ""
        let verifyBinding label expectedRepository candidate merge (binding: RevisionBinding) =
            if binding.Candidate <> candidate || binding.Merge <> merge then findings.Add(RevisionRelationInvalid($"%s{label} binding does not match its named candidate and merge"))
            if binding.Schema <> RevisionBindingSchema || binding.Repository <> expectedRepository then findings.Add(RevisionRelationInvalid($"%s{label} binding does not name the authoritative repository"))
            if not (revision.IsMatch binding.CandidateTree) || not (revision.IsMatch binding.MergeTree) then findings.Add(RevisionRelationInvalid($"%s{label} binding tree identity is invalid"))
            if binding.CandidateTree <> binding.MergeTree then findings.Add(RevisionRelationInvalid($"%s{label} merge does not preserve the candidate tree"))
            let expectedCommand = digest (utf8 (canonicalRevisionCommand binding.Repository binding.Candidate binding.Merge))
            if binding.CommandSha256 <> expectedCommand || binding.ExitCode <> 0 then findings.Add(RevisionRelationInvalid($"%s{label} binding lacks a successful canonical Git observation"))
            if not (sha.IsMatch binding.Digest) || digest (utf8 (revisionBindingPayload binding false)) <> binding.Digest then findings.Add(RevisionRelationInvalid($"%s{label} binding digest is invalid"))
        verifyBinding "implementation" implementationRepository input.Identities.ImplementationCandidate input.Identities.ImplementationMerge input.ImplementationBinding
        verifyBinding "acceptance" acceptanceRepository input.Identities.AcceptanceCandidate input.Identities.AcceptanceMerge input.AcceptanceBinding
        match DateTimeOffset.TryParse input.AcceptedAt with true, _ -> () | _ -> findings.Add(InvalidIdentity("acceptedAt", input.AcceptedAt))
        if findings.Count = 0 then Ok(AcceptanceCandidate input)
        else Error(List.ofSeq findings)

    let private parseIdentities (value: JsonElement) : RevisionIdentities =
        strict "identities" [ "implementationPullRequest"; "implementationCandidate"; "implementationMerge"; "acceptancePullRequest"; "acceptanceCandidate"; "acceptanceMerge"; "protectedMain" ] value
        { ImplementationPullRequest = (prop "implementationPullRequest" value).GetInt32()
          ImplementationCandidate = text "identities" "implementationCandidate" value; ImplementationMerge = text "identities" "implementationMerge" value
          AcceptancePullRequest = (prop "acceptancePullRequest" value).GetInt32()
          AcceptanceCandidate = text "identities" "acceptanceCandidate" value; AcceptanceMerge = text "identities" "acceptanceMerge" value
          ProtectedMain = text "identities" "protectedMain" value }

    let private parseBinding label (value: JsonElement) : RevisionBinding =
        parseRevisionBinding (utf8 (value.GetRawText()))
        |> Result.defaultWith (fun errors ->
            let reason = String.concat "; " errors
            raise (FormatException($"%s{label}: %s{reason}")))

    let canonicalAcceptanceInput (input: AcceptanceInput) =
        let observations =
            input.SddObservations
            |> List.map (fun value -> {| stage = value.Stage; subjectRevision = value.SubjectRevision; artifactJson = value.ArtifactJson |})
        JsonSerializer.SerializeToUtf8Bytes
            {| schema = input.Schema
               plan = JsonNode.Parse(canonicalPlan input.Plan)
               preparationApplication = JsonNode.Parse(canonicalPreparationApplication input.PreparationApplication)
               qualification = JsonNode.Parse(Qualification.canonicalResult input.Qualification)
               lifecycleRunId = input.LifecycleRunId; lifecycleUnitId = input.LifecycleUnitId
               lifecycleLog = input.LifecycleLog; requiredLifecyclePhases = input.RequiredLifecyclePhases
               lifecycleUsageReceipts = input.LifecycleUsageReceipts; lifecycleHistoryReport = input.LifecycleHistoryReport
               reviewEvidence = input.ReviewEvidence; structuredReviewEvidence = input.StructuredReviewEvidence; reviewCycleId = input.ReviewCycleId
               reviewReceipt = input.ReviewReceipt; sddWorkId = input.SddWorkId; sddObservations = observations
               identities = {| implementationPullRequest = input.Identities.ImplementationPullRequest; implementationCandidate = input.Identities.ImplementationCandidate; implementationMerge = input.Identities.ImplementationMerge; acceptancePullRequest = input.Identities.AcceptancePullRequest; acceptanceCandidate = input.Identities.AcceptanceCandidate; acceptanceMerge = input.Identities.AcceptanceMerge; protectedMain = input.Identities.ProtectedMain |}
               implementationBinding = JsonNode.Parse(canonicalRevisionBinding input.ImplementationBinding)
               acceptanceBinding = JsonNode.Parse(canonicalRevisionBinding input.AcceptanceBinding)
               acceptedAt = input.AcceptedAt |}
        |> canonical
        |> fun value -> value + "\n"

    let parseAcceptanceInput (bytes: byte array) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory bytes)
            let root = document.RootElement
            strict "acceptanceInput" [ "schema"; "plan"; "preparationApplication"; "qualification"; "lifecycleRunId"; "lifecycleUnitId"; "lifecycleLog"; "requiredLifecyclePhases"; "lifecycleUsageReceipts"; "lifecycleHistoryReport"; "reviewEvidence"; "structuredReviewEvidence"; "reviewCycleId"; "reviewReceipt"; "sddWorkId"; "sddObservations"; "identities"; "implementationBinding"; "acceptanceBinding"; "acceptedAt" ] root
            let plan = parsePlan (utf8 ((prop "plan" root).GetRawText())) |> Result.defaultWith (String.concat "; " >> FormatException >> raise)
            let preparationApplication = parsePreparationApplication (utf8 ((prop "preparationApplication" root).GetRawText())) |> Result.defaultWith (String.concat "; " >> FormatException >> raise)
            let qualification = Qualification.parseResult (utf8 ((prop "qualification" root).GetRawText())) |> Result.defaultWith (String.concat "; " >> FormatException >> raise)
            let observations = array "sddObservations" root |> List.mapi (fun index item -> strict $"sddObservations[%d{index}]" [ "stage"; "subjectRevision"; "artifactJson" ] item; { Stage = text "sddObservation" "stage" item; SubjectRevision = text "sddObservation" "subjectRevision" item; ArtifactJson = text "sddObservation" "artifactJson" item })
            Ok { Schema = text "acceptanceInput" "schema" root; Plan = plan; PreparationApplication = preparationApplication; Qualification = qualification
                 LifecycleRunId = text "acceptanceInput" "lifecycleRunId" root; LifecycleUnitId = text "acceptanceInput" "lifecycleUnitId" root
                 LifecycleLog = text "acceptanceInput" "lifecycleLog" root; RequiredLifecyclePhases = strings "acceptanceInput" "requiredLifecyclePhases" root
                 LifecycleUsageReceipts = strings "acceptanceInput" "lifecycleUsageReceipts" root
                 LifecycleHistoryReport = text "acceptanceInput" "lifecycleHistoryReport" root
                 ReviewEvidence = text "acceptanceInput" "reviewEvidence" root
                 StructuredReviewEvidence = text "acceptanceInput" "structuredReviewEvidence" root
                 ReviewCycleId = text "acceptanceInput" "reviewCycleId" root
                 ReviewReceipt = text "acceptanceInput" "reviewReceipt" root
                 SddWorkId = text "acceptanceInput" "sddWorkId" root; SddObservations = observations
                 Identities = parseIdentities (prop "identities" root)
                 ImplementationBinding = parseBinding "implementationBinding" (prop "implementationBinding" root)
                 AcceptanceBinding = parseBinding "acceptanceBinding" (prop "acceptanceBinding" root)
                 AcceptedAt = text "acceptanceInput" "acceptedAt" root }
        with error -> Error [ $"invalid roadmap acceptance input: %s{error.Message}" ]

    let candidateDigest (AcceptanceCandidate input) = canonicalAcceptanceInput input |> utf8 |> digest

    let observeAcceptance (AcceptanceCandidate input) = ObservedAcceptance input
    let sealObservedAcceptance (ObservedAcceptance input) = buildAcceptance input
    let acceptedDigest accepted = accepted.Digest
    let acceptedBundle accepted = accepted.BundleJson

    let verifyObservedAcceptance (ObservedAcceptance input) (bundle: byte array) =
        let accepted = buildAcceptance input
        match CanonicalJson.canonicalize bundle with
        | Error reason -> Error [ AcceptanceBundleInvalid reason ]
        | Ok observed when observed + "\n" = accepted.BundleJson -> Ok accepted
        | Ok _ -> Error [ AcceptanceBundleInvalid "bundle bytes differ from the canonical sealed receipt and evidence index" ]

    let acceptedReceipt (accepted: Accepted) = utf8 accepted.ReceiptJson
