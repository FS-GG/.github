namespace FS.GG.Coord.GitHub

module V2Projection =

    open System
    open System.Text.Json
    open System.Text
    open System.Security.Cryptography
    open System.Buffers.Binary
    open Errors
    open Transport
    open Board

    type SelectFieldBinding =
        {
            Id: string
            Options: Map<string, string>
        }

    type ProgrammeEnrollment = { NonceHex: string; PayloadBase64: string }

    type Binding =
        {
            ProgrammeEnrollment: ProgrammeEnrollment option
            BindingVersion: int
            ImportRecipeRevision: string
            ImportArtifactSha256: string
            SelectedIssues: Map<string, string>
            SchemaVersion: int
            RecipeRevision: string
            PopulationRevision: string
            OrganizationId: string
            ArtifactSha256: string
            OwnerKind: OwnerKind
            Owner: string
            ProjectNumber: int
            ProjectTitle: string
            ProjectId: string
            Status: SelectFieldBinding
            RoadmapFieldId: string
            Track: SelectFieldBinding
            Observation: SelectFieldBinding
            WorkLevel: SelectFieldBinding option
            Repositories: Set<string>
        }

    let isProgrammeBinding (binding: Binding) = binding.BindingVersion = 4 || binding.BindingVersion = 5

    type IssueRef =
        {
            Owner: string
            Repository: string
            Number: int
        }

    type SourceVerification =
        | Current of observedRevision: string
        | Stale of lastVerifiedRevision: string option
        | Refused of reason: string

    type SourceVerifier = IssueRef -> IoResult<SourceVerification>

    type Request =
        {
            Issue: IssueRef
            ExpectedNodeId: string
        }

    type Outcome =
        | AlreadyCurrent of itemId: string
        | Updated of itemId: string

    type Report =
        {
            ProjectId: string
            RecipeRevision: string
            Issue: IssueRef
            ObservedRevision: string
            Observation: string
            Outcome: Outcome
            SourceChecks: int
            ProjectReads: int
            Mutations: int
            VerifiedAt: DateTimeOffset
        }

    let private invalid subject message = Error(Http(422, $"%s{subject}: %s{message}"))

    let private nonBlank value = not (String.IsNullOrWhiteSpace value)

    let private canonicalRepository (value: string) =
        let parts = value.Split('/', StringSplitOptions.None)

        parts.Length = 2
        && Array.forall nonBlank parts
        && Array.forall (fun (part: string) -> part.Trim() = part) parts

    let private expectedStatus = Set.ofList [ "Backlog"; "Ready"; "In progress"; "Blocked"; "Done" ]
    let private expectedTrack = Set.ofList [ "Active delivery"; "Follow-up" ]
    let private expectedObservation = Set.ofList [ "Verified"; "Stale"; "Unknown" ]

    let private validateSelect subject expected (field: SelectFieldBinding) =
        if not (nonBlank field.Id) then
            invalid subject "field id is missing"
        elif (field.Options |> Map.keys |> Set.ofSeq) <> expected then
            invalid subject "option names do not match schema v1"
        elif field.Options |> Map.values |> Seq.exists (nonBlank >> not) then
            invalid subject "an option id is missing"
        elif (field.Options |> Map.values |> Set.ofSeq |> Set.count) <> field.Options.Count then
            invalid subject "option ids are duplicated"
        else
            Ok()

    let private productPopulation (binding: Binding) =
        if binding.Repositories.Count <> 1 then false
        else
            let repository = Set.minElement binding.Repositories
            let prefix = repository + "#"
            repository.StartsWith(binding.Owner + "/", StringComparison.Ordinal)
            && canonicalRepository repository
            && (binding.SelectedIssues |> Map.values |> Set.ofSeq |> Set.count) = binding.SelectedIssues.Count
            && (binding.SelectedIssues |> Map.forall (fun node issue ->
                let mutable number = 0
                nonBlank node && nonBlank issue && issue.StartsWith(prefix, StringComparison.Ordinal)
                && Int32.TryParse(issue.Substring(prefix.Length), &number) && number > 0
                && issue = prefix + string number))

    let private strictUtf8 = UTF8Encoding(false, true)
    let private enrollmentSubject = "programme enrollment"
    let private enrollmentFailure () = Error(Malformed(enrollmentSubject, "invalid-private-enrollment"))
    let private closedObject (names: string list) (value: JsonElement) =
        if value.ValueKind <> JsonValueKind.Object then invalidArg "enrollment" "object-required"
        let actual = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
        if actual.Length <> names.Length || Set.ofList actual <> Set.ofList names then invalidArg "enrollment" "closed-schema-required"
    let private strictDocument (limit: int) (bytes: byte array) =
        if bytes.Length = 0 || bytes.Length > limit || (bytes.Length >= 3 && bytes[0] = 239uy && bytes[1] = 187uy && bytes[2] = 191uy) then
            invalidArg "enrollment" "bounded-utf8-required"
        strictUtf8.GetString bytes |> ignore
        let document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(MaxDepth = 16))
        try
            let rec unique (value: JsonElement) =
                match value.ValueKind with
                | JsonValueKind.Object ->
                    let names = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
                    for entry in value.EnumerateObject() do
                        if not (names.Add entry.Name) then invalidArg "enrollment" "duplicate-key"
                        unique entry.Value
                | JsonValueKind.Array -> for entry in value.EnumerateArray() do unique entry
                | _ -> ()
            unique document.RootElement
            document
        with _ -> document.Dispose(); reraise()
    let private privateBytes (value: ProgrammeEnrollment) =
        if isNull value.NonceHex || value.NonceHex.Length <> 64 || value.NonceHex |> Seq.exists (fun character -> not (Char.IsAsciiHexDigitLower character)) then
            invalidArg "enrollment" "nonce-required"
        if isNull value.PayloadBase64 || value.PayloadBase64.Length = 0 || value.PayloadBase64.Length > 174764 then invalidArg "enrollment" "payload-bound"
        let bytes = Convert.FromBase64String value.PayloadBase64
        if Convert.ToBase64String bytes <> value.PayloadBase64 then invalidArg "enrollment" "canonical-base64-required"
        use document = strictDocument 131072 bytes
        bytes

    let programmeCommitment (value: ProgrammeEnrollment) : IoResult<string> =
        try
            let payload = privateBytes value
            let nonce = Convert.FromHexString value.NonceHex
            let prefix = Encoding.ASCII.GetBytes "fsgg.coordination-board-v2-enrollment/v3\u0000"
            let length = Array.zeroCreate<byte> 8
            BinaryPrimitives.WriteUInt64BigEndian(length.AsSpan(), uint64 payload.Length)
            let bytes = Array.concat [ prefix; nonce; length; payload ]
            Ok(Convert.ToHexStringLower(SHA256.HashData bytes))
        with _ -> enrollmentFailure()

    // The programme population ceiling. It was 100 (#4941), then 250 (coherent 0.114.0), and is 500 (#5119): the
    // Coordination board and its readers are limited to 500 items. Both the private payload row count and the binding's
    // selected-issue count are bounded by this one value. The byte bounds (128KiB payload, 256KiB binding) are unchanged;
    // the cohort-90 binding measured 37,938 payload and 62,651 binding bytes at 207 rows, about 92KiB and 150KiB at 500.
    let legacyProgrammeRowCap = 100
    // The Coordination board item limit (#5119): the complete-membership read refuses a larger board.
    let maximumBoardItems = 500
    let previousProgrammeRowCap = 250
    let maximumProgrammeRows = 500

    // The request ceiling of the CLI's bounded transport, scaled by the selected population (#5119). It was a flat 500
    // sends, which the 207-row cohort-90 inspection exhausted (2,502 reads, all rows Unknown). The transport is shared by
    // `inspect` and `refresh`, but this scaling is derived from INSPECT measurements only. With blockers and Work level
    // folded into the per-row reads and 25 reads per batch, the fixture inspection costs well under one send per row; 2 per
    // row plus a fixed 64 keeps headroom for real pagination and PR joins, and the old 500 stays the floor. A programme
    // `refresh` is batched the same way (#5131): 42 fixture sends for 207 already-Verified rows, including the fresh
    // no-op confirmation phase, plus one unbatched one-shot run of about 9 reads, the send and the readback for each row
    // it actually writes. Above
    // maximumProgrammeRows the binding is refused before any send, so the ceiling never needs the value beyond that cap.
    let refreshRequestCeiling (selectedRows: int) = max 500 (64 + 2 * min (max selectedRows 0) maximumProgrammeRows)

    // The deadline of the same bounded transport, scaled the same way and also from inspect measurements only (#5119).
    // It was a flat 270 seconds. The live 207-row cohort-90 inspection took 120 to 126 seconds once the reads per row were
    // cut, which projects to about 305 seconds at 500 rows. 60 seconds plus 0.7 per selected row, never below 270, is 410
    // seconds at the 500-row cap, which leaves the existing 30-second transport timeout inside the refresh job's 8-minute
    // limit. A batched programme refresh fits it the same way; each row it writes adds its own one-shot run (#5131).
    let refreshDeadlineSeconds (selectedRows: int) = max 270.0 (60.0 + 0.7 * float (min (max selectedRows 0) maximumProgrammeRows))

    let private payloadRows (binding: Binding) (bytes: byte array) : IoResult<(string * string * string) list> =
        try
            use document = strictDocument 131072 bytes
            let root = document.RootElement
            closedObject [ "schema"; "target"; "binding"; "items" ] root
            if root.GetProperty("schema").GetString() <> "fsgg.coordination-board-v2-enrollment-payload/v1" then invalidArg "enrollment" "schema"
            let target = root.GetProperty "target"
            closedObject [ "owner"; "organizationId"; "projectId"; "projectNumber"; "schemaVersion" ] target
            let admission = root.GetProperty "binding"
            closedObject [ "recipeRevision"; "artifactSha256"; "repositories" ] admission
            let repositories = admission.GetProperty("repositories").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
            if target.GetProperty("owner").GetString() <> binding.Owner || target.GetProperty("organizationId").GetString() <> binding.OrganizationId
               || target.GetProperty("projectId").GetString() <> binding.ProjectId || target.GetProperty("projectNumber").GetInt32() <> binding.ProjectNumber
               || target.GetProperty("schemaVersion").GetInt32() <> binding.SchemaVersion
               || admission.GetProperty("recipeRevision").GetString() <> binding.ImportRecipeRevision
               || admission.GetProperty("artifactSha256").GetString() <> binding.ImportArtifactSha256
               || repositories.Length <> binding.Repositories.Count || Set.ofList repositories <> binding.Repositories then invalidArg "enrollment" "scope"
            let rows: (string * string * string) list =
                root.GetProperty("items").EnumerateArray() |> Seq.map (fun row ->
                    closedObject [ "issue"; "nodeId"; "roadmap" ] row
                    let issue = row.GetProperty("issue").GetString()
                    let node = row.GetProperty("nodeId").GetString()
                    let roadmap = row.GetProperty("roadmap").GetString()
                    if String.IsNullOrWhiteSpace node || String.IsNullOrWhiteSpace roadmap || roadmap.Length > 4096
                       || Map.tryFind node binding.SelectedIssues <> Some issue then invalidArg "enrollment" "row"
                    issue, node, roadmap) |> Seq.toList
            if rows.Length <> binding.SelectedIssues.Count || rows.IsEmpty || rows.Length > maximumProgrammeRows
               || (rows |> List.map (fun (_, node, _) -> node) |> Set.ofList |> Set.count) <> rows.Length
               || (rows |> List.map (fun (issue, _, _) -> issue) |> Set.ofList |> Set.count) <> rows.Length then invalidArg "enrollment" "cohort"
            Ok rows
        with _ -> enrollmentFailure()

    let programmeRows (binding: Binding) : IoResult<(string * string * string) list> =
        try
            match binding.ProgrammeEnrollment with
            | Some value when isProgrammeBinding binding -> privateBytes value |> payloadRows binding
            | _ -> enrollmentFailure()
        with _ -> enrollmentFailure()

    let decodeProgrammePublicCommitment (publicJson: string) : IoResult<string> =
        try
            use document = strictDocument 1024 (strictUtf8.GetBytes publicJson)
            let root = document.RootElement
            closedObject [ "schema"; "commitmentSha256" ] root
            let expected = root.GetProperty("commitmentSha256").GetString()
            if root.GetProperty("schema").GetString() <> "fsgg.coordination-board-v2-enrollment/v3"
               || isNull expected || expected.Length <> 64 || expected |> Seq.exists (fun character -> not (Char.IsAsciiHexDigitLower character)) then invalidArg "enrollment" "public-schema"
            Ok expected
        with _ -> enrollmentFailure()

    let authenticateProgrammeEnrollment (binding: Binding) (publicJson: string) : IoResult<(string * string * string) list> =
        decodeProgrammePublicCommitment publicJson |> Result.bind (fun expected ->
            match binding.ProgrammeEnrollment with
            | Some value ->
                programmeCommitment value |> Result.bind (fun actual ->
                    if actual <> expected then enrollmentFailure() else programmeRows binding)
            | None -> enrollmentFailure())

    let private validateBindingCore (binding: Binding) =
        let subject = "the Coordination V2 binding"
        let allFieldIds = [ binding.Status.Id; binding.RoadmapFieldId; binding.Track.Id; binding.Observation.Id ] @ (binding.WorkLevel |> Option.map (fun value -> [value.Id]) |> Option.defaultValue [])

        if binding.BindingVersion <> 2 && binding.BindingVersion <> 3 && not (isProgrammeBinding binding) then
            invalid subject "only historical organization v2, product v3 or explicit programme v4/v5 is supported"
        elif String.IsNullOrWhiteSpace binding.ImportRecipeRevision || binding.ImportRecipeRevision.Length <> 40 || binding.ImportRecipeRevision |> Seq.exists (Uri.IsHexDigit >> not)
             || String.IsNullOrWhiteSpace binding.ImportArtifactSha256 || binding.ImportArtifactSha256.Length <> 64 || binding.ImportArtifactSha256 |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "distinct reviewed import constructor provenance is incomplete"
        elif binding.SelectedIssues.Count < (if binding.BindingVersion = 2 then 3 else 1) || binding.SelectedIssues.Count > (if isProgrammeBinding binding then maximumProgrammeRows else 5) || binding.SelectedIssues |> Map.exists (fun node issue -> String.IsNullOrWhiteSpace node || String.IsNullOrWhiteSpace issue) then
            invalid subject "explicit selected native cohort is incomplete"
        elif binding.SchemaVersion <> (if binding.BindingVersion = 5 then 3 elif binding.BindingVersion = 4 then 2 else 1) then
            invalid subject "binding and schema versions disagree"
        elif not (nonBlank binding.RecipeRevision) || binding.RecipeRevision.Length <> 40 || binding.RecipeRevision |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "the reviewed recipe revision must be an immutable Git SHA"
        elif not (nonBlank binding.PopulationRevision) || binding.PopulationRevision.Length <> 40 || binding.PopulationRevision |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "the reviewed population revision must be an immutable Git SHA"
        elif not (nonBlank binding.OrganizationId) || not (nonBlank binding.ArtifactSha256) || binding.ArtifactSha256.Length <> 64 || binding.ArtifactSha256 |> Seq.exists (Uri.IsHexDigit >> not) then
            invalid subject "immutable organization or reviewed artifact identity is missing"
        elif binding.OwnerKind <> OwnerKind.Org then
            invalid subject "this organization-board adapter requires an explicit organization owner kind"
        elif not (nonBlank binding.Owner) || binding.ProjectNumber <= 0 || not (nonBlank binding.ProjectTitle) || not (nonBlank binding.ProjectId) then
            invalid subject "the exact organization project identity is incomplete"
        elif
            (String.Equals(binding.Owner, "FS-GG", StringComparison.OrdinalIgnoreCase)
             && (binding.ProjectNumber = 1 || binding.ProjectId = "PVT_kwDOEYAWY84Bb08W"))
            || (binding.BindingVersion = 3 && binding.ProjectId = "PVT_kwDOEYAWY84Bb08W")
        then
            invalid subject "the legacy Coordination Project 1 identity is not a V2 projection target"
        elif (binding.BindingVersion = 2 || isProgrammeBinding binding) && (binding.Owner <> "FS-GG" || binding.ProjectTitle <> "Coordination V2") then
            invalid subject "this restricted organization pilot requires the FS-GG Coordination V2 target"
        elif binding.BindingVersion = 3 && (binding.ProjectId = "PVT_kwDOEYAWY84Bldpa" || (binding.Owner = "FS-GG" && binding.ProjectNumber = 3)) then
            invalid subject "a repository binding cannot select the organization Coordination V2 target"
        elif isProgrammeBinding binding && (binding.ProjectNumber <> 3 || binding.ProjectId <> "PVT_kwDOEYAWY84Bldpa" || binding.OrganizationId <> "O_kgDOEYAWYw"
                                           || (binding.Repositories |> Set.exists (fun repo -> not (repo.StartsWith("FS-GG/", StringComparison.Ordinal))))
                                           || (binding.SelectedIssues |> Map.exists (fun _ value ->
                                               let hash = value.LastIndexOf '#'
                                               let mutable number = 0
                                               hash <= 0 || not (binding.Repositories.Contains(value.Substring(0, max 0 hash)))
                                               || not (Int32.TryParse(value.Substring(hash + 1), &number)) || number <= 0
                                               || value <> value.Substring(0, hash) + "#" + string number))
                                           || (binding.SelectedIssues |> Map.values |> Set.ofSeq |> Set.count) <> binding.SelectedIssues.Count) then
            invalid subject "programme enrollment requires exact Project 3 identities and distinct allowed native issues"
        elif binding.BindingVersion = 3 && not (productPopulation binding) then
            invalid subject "a repository binding requires one owner-local repository and an exact bounded native cohort"
        elif not (nonBlank binding.RoadmapFieldId) then
            invalid subject "the Roadmap field id is missing"
        elif (binding.BindingVersion = 5) <> binding.WorkLevel.IsSome then
            invalid subject "Work level is required only for the explicit version 5 successor"
        elif List.distinct allFieldIds |> List.length <> allFieldIds.Length then
            invalid subject "field ids are duplicated"
        elif Set.isEmpty binding.Repositories || binding.Repositories |> Seq.exists (canonicalRepository >> not) then
            invalid subject "the repository allowlist is empty or non-canonical"
        else
            match validateSelect "Status" (if isProgrammeBinding binding then expectedStatus.Add "In review" else expectedStatus) binding.Status with
            | Error error -> Error error
            | Ok() ->
                match validateSelect "Track" expectedTrack binding.Track with
                | Error error -> Error error
                | Ok() ->
                    validateSelect "Observation" expectedObservation binding.Observation |> Result.bind (fun () ->
                        match binding.WorkLevel with
                        | None -> Ok()
                        | Some field ->
                            validateSelect "Work level" (Set.ofList ["Outcome"; "Delivery"]) field |> Result.bind (fun () ->
                                let existing = [binding.Status; binding.Track; binding.Observation] |> List.collect (fun value -> value.Options |> Map.values |> Seq.toList) |> Set.ofList
                                if field.Options |> Map.values |> Seq.exists existing.Contains then invalid subject "Work level option ids overlap another field" else Ok()))

    let validateBinding (binding: Binding) =
        validateBindingCore binding |> Result.bind (fun () ->
            if isProgrammeBinding binding then programmeRows binding |> Result.map ignore
            elif binding.ProgrammeEnrollment.IsSome then enrollmentFailure()
            else Ok())

    let private text (name: string) (element: JsonElement) = element.GetProperty(name).GetString()
    let private number (name: string) (element: JsonElement) = element.GetProperty(name).GetInt32()
    let private closed = closedObject

    let private decodeBindingCore (allowDraft: bool) (json: string) =
        try
            if isNull json || json.Length = 0 || json.Length > 262144 then invalidArg "binding" "binding-byte-bound"
            use document = strictDocument 262144 (strictUtf8.GetBytes json)
            let root = document.RootElement
            let version = number "bindingVersion" root
            if version = 4 && number "schemaVersion" root <> 2 then Error "invalid binding: binding and schema versions disagree"
            else
                let fields = [ "bindingVersion"; "importRecipeRevision"; "importArtifactSha256"; "selectedIssues"; "schemaVersion"; "recipeRevision"; "populationRevision"; "organizationId"; "artifactSha256"; "ownerKind"; "owner"; "projectNumber"; "projectTitle"; "projectId"; "status"; "roadmapFieldId"; "track"; "observation"; "repositories" ]
                let fields = if version = 5 then fields @ ["workLevel"] else fields
                closed (if (version = 4 || version = 5) && not allowDraft then fields @ [ "programmeEnrollment" ] else fields) root
                if text "ownerKind" root <> "organization" then invalidArg "ownerKind" "only organization is admitted"
                let field (name: string) =
                    let value = root.GetProperty(name)
                    closed [ "id"; "options" ] value
                    let options = value.GetProperty("options").EnumerateObject() |> Seq.map (fun item -> item.Name, item.Value.GetString()) |> Seq.toList
                    if options.Length <> (options |> Map.ofList |> Map.count) then invalidArg "options" "duplicate options"
                    { Id = text "id" value; Options = Map.ofList options }
                let repositories = root.GetProperty("repositories").EnumerateArray() |> Seq.map _.GetString() |> Seq.toList
                if repositories.Length <> (repositories |> Set.ofList |> Set.count) then invalidArg "repositories" "duplicate repositories"
                let selected = root.GetProperty("selectedIssues").EnumerateObject() |> Seq.map (fun entry -> entry.Name, entry.Value.GetString()) |> Seq.toList
                if selected.Length <> (selected |> Map.ofList |> Map.count) then invalidArg "selectedIssues" "duplicate native issue IDs"
                let binding =
                    { ProgrammeEnrollment =
                        (if (version = 4 || version = 5) && not allowDraft then
                            let value = root.GetProperty "programmeEnrollment"
                            closed [ "nonceHex"; "payloadBase64" ] value
                            Some { NonceHex = text "nonceHex" value; PayloadBase64 = text "payloadBase64" value }
                         else None)
                      BindingVersion = version; ImportRecipeRevision = text "importRecipeRevision" root
                      ImportArtifactSha256 = text "importArtifactSha256" root
                      SelectedIssues = Map.ofList selected
                      SchemaVersion = number "schemaVersion" root; RecipeRevision = text "recipeRevision" root
                      PopulationRevision = text "populationRevision" root; OrganizationId = text "organizationId" root
                      ArtifactSha256 = text "artifactSha256" root; OwnerKind = Org; Owner = text "owner" root
                      ProjectNumber = number "projectNumber" root; ProjectTitle = text "projectTitle" root; ProjectId = text "projectId" root
                      Status = field "status"; RoadmapFieldId = text "roadmapFieldId" root; Track = field "track"
                      Observation = field "observation"; WorkLevel = (if version = 5 then Some(field "workLevel") else None); Repositories = Set.ofList repositories }
                // Version 3 uses this same closed wire shape for a selected receiver repository.
                // Local-only workspaces select no board command; no implicit binding is constructed.
                (if allowDraft then validateBindingCore binding else validateBinding binding) |> Result.map (fun () -> binding) |> Result.mapError Errors.explain
        with _ -> Error "invalid binding: unknown, missing or duplicate properties; closed bounded schema or private enrollment refused"

    let decodeBinding (json: string) = decodeBindingCore false json

    let prepareProgrammeBinding (bindingTemplateJson: string) (payload: byte array) (nonceFactory: unit -> byte array) : IoResult<string * string> =
        try
            match decodeBindingCore true bindingTemplateJson with
            | Error _ -> enrollmentFailure()
            | Ok binding when not (isProgrammeBinding binding) -> enrollmentFailure()
            | Ok binding ->
                payloadRows binding payload |> Result.bind (fun _ ->
                    let encoded = Convert.ToBase64String payload
                    let privateEnvelope (nonceHex: string) =
                        let root = System.Text.Json.Nodes.JsonNode.Parse bindingTemplateJson
                        let privateNode = System.Text.Json.Nodes.JsonObject()
                        privateNode.Add("nonceHex", System.Text.Json.Nodes.JsonValue.Create nonceHex)
                        privateNode.Add("payloadBase64", System.Text.Json.Nodes.JsonValue.Create encoded)
                        root.AsObject().Add("programmeEnrollment", privateNode)
                        root.ToJsonString()
                    // Fixed-width internal sizing witness only; never an emitted or admitted nonce.
                    let sizingWitness = privateEnvelope (String.replicate 64 "0")
                    match decodeBinding sizingWitness with
                    | Error _ -> enrollmentFailure()
                    | Ok _ ->
                        let nonce = nonceFactory()
                        if isNull nonce || nonce.Length <> 32 then enrollmentFailure()
                        else
                            let enrollment: ProgrammeEnrollment = { NonceHex = Convert.ToHexStringLower nonce; PayloadBase64 = encoded }
                            programmeCommitment enrollment |> Result.map (fun commitment ->
                                let publicNode = System.Text.Json.Nodes.JsonObject()
                                publicNode.Add("schema", System.Text.Json.Nodes.JsonValue.Create "fsgg.coordination-board-v2-enrollment/v3")
                                publicNode.Add("commitmentSha256", System.Text.Json.Nodes.JsonValue.Create commitment)
                                privateEnvelope enrollment.NonceHex, publicNode.ToJsonString()))
        with _ -> enrollmentFailure()

    let private sameOptions expected actual = expected = actual

    let private verifyFieldSchema (binding: Binding) (board: BoardMap) =
        let select name expected =
            match Map.tryFind name board.Fields with
            | Some { Id = id; Type = SingleSelect options } when id = expected.Id && sameOptions options expected.Options -> Ok()
            | _ -> Error(Malformed($"the Coordination V2 field '%s{name}'", "field identity, type or options drifted from the reviewed binding"))

        match select "Status" binding.Status with
        | Error error -> Error error
        | Ok() ->
            match Map.tryFind "Roadmap" board.Fields with
            | Some { Id = id; Type = Text } when id = binding.RoadmapFieldId ->
                match select "Track" binding.Track with
                | Error error -> Error error
                | Ok() -> select "Observation" binding.Observation |> Result.bind (fun () -> match binding.WorkLevel with Some field -> select "Work level" field | None -> Ok())
            | _ -> Error(Malformed("the Coordination V2 field 'Roadmap'", "field identity or type drifted from the reviewed binding"))

    let private graphRequest document variables subject : Transport.Request =
        { Method = "POST"; Path = "graphql"; Query = []; Body = Query(document, variables)
          Budget = GraphQl; IfNoneMatch = None; Subject = subject }

    let private verifyOrganization transport (binding: Binding) =
        let document = "query($owner: String!) { organization(login: $owner) { id login } rateLimit { cost remaining } }"
        GraphQl.read transport (graphRequest document [ "owner", VString binding.Owner ] "immutable project owner") (fun data ->
            let organization = data.GetProperty "organization"
            if organization.GetProperty("id").GetString() <> binding.OrganizationId || organization.GetProperty("login").GetString() <> binding.Owner then
                Error(Malformed("immutable project owner", "organization identity differs from the reviewed binding"))
            else Ok())

    // The one-shot writer's explicit refusal for an archived card (#5119); documented in the .fsi.
    let archivedCardWriteRefusal = "the board card is archived (Done retention); no Observation write"

    let private exactMembership (transport: IGitHubTransport) (binding: Binding) (issue: IssueRef) expectedNativeId =
        let subject = $"{issue.Owner}/{issue.Repository}#{issue.Number} exact membership"
        let document =
            "query($owner: String!, $repo: String!, $number: Int!, $after: String) { repository(owner: $owner, name: $repo) { nameWithOwner issue(number: $number) { id number projectItems(first: 50, after: $after) { totalCount pageInfo { hasNextPage endCursor } nodes { id isArchived project { id } content { ... on Issue { id number repository { nameWithOwner } } } } } } } rateLimit { cost remaining } }"
        let mutable pages = 0
        let fetch after =
            let variables =
                [ "owner", VString issue.Owner; "repo", VString issue.Repository; "number", VNumber(double issue.Number) ]
                @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
            GraphQl.read transport (graphRequest document variables subject) (fun data ->
                try
                    let repository = data.GetProperty "repository"
                    let native = repository.GetProperty "issue"
                    let repo = $"{issue.Owner}/{issue.Repository}"
                    let nativeId = native.GetProperty("id").GetString()
                    if repository.GetProperty("nameWithOwner").GetString() <> repo || native.GetProperty("number").GetInt32() <> issue.Number || nativeId <> expectedNativeId || not (nonBlank nativeId) then
                        Error(Malformed(subject, "native issue identity differs"))
                    else
                        let decode (node: JsonElement) =
                            let itemId = node.GetProperty("id").GetString()
                            let projectId = node.GetProperty("project").GetProperty("id").GetString()
                            let content = node.GetProperty "content"
                            if not (nonBlank itemId) || not (nonBlank projectId) || content.GetProperty("id").GetString() <> nativeId || content.GetProperty("number").GetInt32() <> issue.Number || content.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> repo then
                                Error(Malformed(subject, "membership content identity differs"))
                            else Ok(itemId, projectId, (nativeId, node.GetProperty("isArchived").GetBoolean()))
                        let connection = native.GetProperty "projectItems"
                        if connection.GetProperty("totalCount").GetInt32() < 0 || connection.GetProperty("nodes").GetArrayLength() > 50 then
                            Error(Malformed(subject, "membership count/window is invalid"))
                        else
                            let page = GraphQl.page subject "native issue project memberships" (fun (id, _, _) -> id) decode connection
                            match page with
                            | Ok _ -> pages <- pages + 1; page
                            | Error _ -> page
                with
                | :? Collections.Generic.KeyNotFoundException
                | :? InvalidOperationException -> Error(Malformed(subject, "unreadable native membership identity")))
        match GraphQl.drain subject "native issue project memberships" { MaxPages = 10; MaxItems = 500 } fetch with
        | Error error -> Error error
        | Ok memberships ->
            match memberships |> List.filter (fun (_, projectId, _) -> projectId = binding.ProjectId) with
            | [ (itemId, _, (nativeId, archived)) ] -> Ok(itemId, nativeId, pages, archived)
            | [] -> Error(NotFound subject)
            | _ -> Error(Malformed(subject, "duplicate exact-project membership"))

    let private fieldValueByItemId (transport: IGitHubTransport) (binding: Binding) (issue: IssueRef) nativeId itemId =
        let subject = $"board item %s{itemId} Observation"
        let document =
            "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } fieldValueByName(name: \"Observation\") { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } rateLimit { cost remaining } }"

        let request =
            {
                Method = "POST"
                Path = "graphql"
                Query = []
                Body = Query(document, [ "itemId", VId itemId ])
                Budget = GraphQl
                IfNoneMatch = None
                Subject = subject
            }

        GraphQl.read transport request (fun data ->
            try
                let node = data.GetProperty "node"

                if node.ValueKind = JsonValueKind.Null then
                    Error(NotFound subject)
                elif node.GetProperty("id").GetString() <> itemId
                     || node.GetProperty("project").GetProperty("id").GetString() <> binding.ProjectId
                     || node.GetProperty("content").GetProperty("id").GetString() <> nativeId
                     || node.GetProperty("content").GetProperty("number").GetInt32() <> issue.Number
                     || node.GetProperty("content").GetProperty("repository").GetProperty("nameWithOwner").GetString() <> $"{issue.Owner}/{issue.Repository}" then
                    Error(Malformed(subject, "immutable item project or content identity differs"))
                else
                    match node.TryGetProperty "fieldValueByName" with
                    | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
                    | true, value when value.ValueKind = JsonValueKind.Object ->
                        match value.TryGetProperty "name" with
                        | true, name when name.ValueKind = JsonValueKind.String && nonBlank (name.GetString()) -> Ok(Some(name.GetString()))
                        | _ -> Error(Malformed(subject, "the Observation value has no readable option name"))
                    | _ -> Error(Malformed(subject, "the response has no readable Observation value"))
            with :? Collections.Generic.KeyNotFoundException ->
                Error(Malformed(subject, "the response is missing data.node")))

    // Complete project population, including unselected issues, PRs and drafts. No enrollment occurs.
    type Membership =
        { ItemId: string; ContentId: string; Kind: string; Issue: IssueRef option; Enrolled: bool; Archived: bool }

    let readMemberships (transport: IGitHubTransport) (binding: Binding) =
        validateBinding binding |> Result.bind (fun () -> verifyOrganization transport binding) |> Result.bind (fun () ->
            let expected: ExactProject = { Owner = binding.Owner; Number = binding.ProjectNumber; Title = binding.ProjectTitle; Id = binding.ProjectId }
            bootstrapExactProject transport expected |> Result.bind (fun board -> verifyFieldSchema binding board) |> Result.bind (fun () ->
                let subject = "complete V2 project memberships"
                let fetch after =
                    let document = "query($projectId: ID!, $after: String) { node(id: $projectId) { ... on ProjectV2 { id items(first: 50, after: $after) { totalCount nodes { id content { __typename ... on Issue { id number repository { nameWithOwner } } ... on PullRequest { id } ... on DraftIssue { id } } } pageInfo { hasNextPage endCursor } } } } rateLimit { cost remaining } }"
                    let variables = [ "projectId", VId binding.ProjectId ] @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
                    GraphQl.read transport (graphRequest document variables subject) (fun data ->
                        let project = data.GetProperty "node"
                        if project.GetProperty("id").GetString() <> binding.ProjectId then Error(Malformed(subject, "project identity differs"))
                        else
                            let decode (node: JsonElement) =
                                let item = node.GetProperty("id").GetString()
                                let content = node.GetProperty "content"
                                if not (nonBlank item) || content.ValueKind = JsonValueKind.Null then Error(Malformed(subject, "item/content inaccessible"))
                                else
                                    let kind = content.GetProperty("__typename").GetString()
                                    let id = content.GetProperty("id").GetString()
                                    let issue =
                                        if kind <> "Issue" then None
                                        else
                                            let repository = content.GetProperty("repository").GetProperty("nameWithOwner").GetString().Split('/')
                                            if repository.Length <> 2 then None
                                            else Some { Owner = repository[0]; Repository = repository[1]; Number = content.GetProperty("number").GetInt32() }
                                    if (kind = "Issue" && issue.IsNone) || not (nonBlank id) || not (List.contains kind [ "Issue"; "PullRequest"; "DraftIssue" ]) || (issue |> Option.exists (fun value -> value.Number <= 0)) then
                                        Error(Malformed(subject, "unsupported or unreadable content identity"))
                                    else
                                        let enrolled = issue |> Option.exists (fun value -> Map.tryFind id binding.SelectedIssues = Some $"{value.Owner}/{value.Repository}#{value.Number}")
                                        if binding.SelectedIssues.ContainsKey id && not enrolled then Error(Malformed(subject, "enrolled node has mismatched native identity"))
                                        else Ok { ItemId = item; ContentId = id; Kind = kind; Issue = issue; Enrolled = enrolled; Archived = false }
                            GraphQl.page subject "project memberships" (fun (value: Membership) -> value.ItemId) decode (project.GetProperty "items"))
                GraphQl.drain subject "project memberships" { MaxPages = 10; MaxItems = maximumBoardItems } fetch |> Result.bind (fun members ->
                    if (members |> List.map _.ContentId |> Set.ofList |> Set.count) <> members.Length then Error(Malformed(subject, "duplicate content memberships"))
                    else Ok members)))

    type PlanningObservation =
        { ItemId: string; Status: string option; Roadmap: string option; Track: string option
          Observation: string option; MembershipPages: int; WorkLevel: (string * string option) option; Archived: bool }

    let private readPlanningLegacy (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        let admitted = validateBinding binding |> Result.bind (fun () ->
            if request.Issue.Number <= 0 || not (binding.Repositories.Contains $"{request.Issue.Owner}/{request.Issue.Repository}") || Map.tryFind request.ExpectedNodeId binding.SelectedIssues <> Some $"{request.Issue.Owner}/{request.Issue.Repository}#{request.Issue.Number}" then
                Error(Malformed("bound V2 planning fields", "request is outside the explicitly selected native cohort"))
            else Ok())
        admitted |> Result.bind (fun () ->
            verifyOrganization transport binding |> Result.bind (fun () ->
                let expected: ExactProject = { Owner = binding.Owner; Number = binding.ProjectNumber; Title = binding.ProjectTitle; Id = binding.ProjectId }
                bootstrapExactProject transport expected |> Result.bind (fun board ->
                    verifyFieldSchema binding board |> Result.bind (fun () ->
                        exactMembership transport binding request.Issue request.ExpectedNodeId |> Result.bind (fun (itemId, nativeId, pages, archived) ->
                            let document = "query($itemId: ID!) { node(id: $itemId) { ... on ProjectV2Item { id project { id } content { ... on Issue { id number repository { nameWithOwner } } } status: fieldValueByName(name: \"Status\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } roadmap: fieldValueByName(name: \"Roadmap\") { __typename ... on ProjectV2ItemFieldTextValue { text field { ... on ProjectV2Field { id } } } } track: fieldValueByName(name: \"Track\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } observation: fieldValueByName(name: \"Observation\") { __typename ... on ProjectV2ItemFieldSingleSelectValue { name optionId field { ... on ProjectV2SingleSelectField { id } } } } } } rateLimit { cost remaining } }"
                            GraphQl.read transport (graphRequest document [ "itemId", VId itemId ] "bound V2 planning fields") (fun data ->
                                let node = data.GetProperty "node"
                                let content = node.GetProperty "content"
                                if node.GetProperty("id").GetString() <> itemId || node.GetProperty("project").GetProperty("id").GetString() <> binding.ProjectId
                                   || content.GetProperty("id").GetString() <> nativeId || content.GetProperty("number").GetInt32() <> request.Issue.Number
                                   || content.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> $"{request.Issue.Owner}/{request.Issue.Repository}" then
                                    Error(Malformed("bound V2 planning fields", "immutable item/project/native identity differs"))
                                else
                                    let select (alias: string) (field: SelectFieldBinding) =
                                        let value = node.GetProperty alias
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        else
                                            let name = value.GetProperty("name").GetString()
                                            if value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldSingleSelectValue"
                                               || value.GetProperty("field").GetProperty("id").GetString() <> field.Id
                                               || Map.tryFind name field.Options <> Some(value.GetProperty("optionId").GetString()) then
                                                Error(Malformed("bound V2 planning fields", "single-select field/value differs from bound schema"))
                                            else Ok(Some name)
                                    let roadmap =
                                        let value = node.GetProperty "roadmap"
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        elif value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldTextValue" || value.GetProperty("field").GetProperty("id").GetString() <> binding.RoadmapFieldId then
                                            Error(Malformed("bound V2 planning fields", "Roadmap field identity/type differs"))
                                        else Ok(Some(value.GetProperty("text").GetString()))
                                    select "status" binding.Status |> Result.bind (fun status -> roadmap |> Result.bind (fun roadmap ->
                                        select "track" binding.Track |> Result.bind (fun track -> select "observation" binding.Observation |> Result.map (fun observation ->
                                            { ItemId = itemId; Status = status; Roadmap = roadmap; Track = track; Observation = observation; MembershipPages = pages; WorkLevel = None; Archived = archived }))))))))))

    // Read the successor's membership and planning values together. Each invocation still
    // reads the exact schema, and every membership page independently joins the owner and
    // native issue. No response is cached across items or across the final barrier.
    let private readSuccessorPlanning (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        let subject = "bound V2 successor planning memberships"
        let invalid detail = Error(Malformed(subject, detail))
        validateBinding binding |> Result.bind (fun () ->
            if request.Issue.Number <= 0 || not (binding.Repositories.Contains $"{request.Issue.Owner}/{request.Issue.Repository}")
               || Map.tryFind request.ExpectedNodeId binding.SelectedIssues <> Some $"{request.Issue.Owner}/{request.Issue.Repository}#{request.Issue.Number}" then
                invalid "request is outside the explicitly selected native cohort"
            else
                let expected: ExactProject = { Owner = binding.Owner; Number = binding.ProjectNumber; Title = binding.ProjectTitle; Id = binding.ProjectId }
                bootstrapExactProject transport expected |> Result.bind (fun board ->
                    verifyFieldSchema binding board |> Result.bind (fun () ->
                        let document = "query($projectOwner:String!,$owner:String!,$repo:String!,$number:Int!,$after:String){organization(login:$projectOwner){id login} repository(owner:$owner,name:$repo){nameWithOwner issue(number:$number){id number projectItems(first:50,after:$after){totalCount pageInfo{hasNextPage endCursor} nodes{id isArchived project{id} content{... on Issue{id number repository{nameWithOwner}}} status:fieldValueByName(name:\"Status\"){__typename ... on ProjectV2ItemFieldSingleSelectValue{name optionId field{... on ProjectV2SingleSelectField{id}}}} roadmap:fieldValueByName(name:\"Roadmap\"){__typename ... on ProjectV2ItemFieldTextValue{text field{... on ProjectV2Field{id}}}} track:fieldValueByName(name:\"Track\"){__typename ... on ProjectV2ItemFieldSingleSelectValue{name optionId field{... on ProjectV2SingleSelectField{id}}}} observation:fieldValueByName(name:\"Observation\"){__typename ... on ProjectV2ItemFieldSingleSelectValue{name optionId field{... on ProjectV2SingleSelectField{id}}}}" + (if binding.WorkLevel.IsSome then " workLevel:fieldValueByName(name:\"Work level\"){__typename ... on ProjectV2ItemFieldSingleSelectValue{name optionId field{... on ProjectV2SingleSelectField{id}}}}" else "") + "}}}} rateLimit{cost remaining}}"
                        let mutable pages = 0
                        let fetch after =
                            let variables =
                                [ "projectOwner", VString binding.Owner; "owner", VString request.Issue.Owner
                                  "repo", VString request.Issue.Repository; "number", VNumber(double request.Issue.Number) ]
                                @ (after |> Option.map (fun cursor -> [ "after", VString cursor ]) |> Option.defaultValue [])
                            GraphQl.read transport (graphRequest document variables subject) (fun data ->
                                try
                                    let organization = data.GetProperty "organization"
                                    let repository = data.GetProperty "repository"
                                    let native = repository.GetProperty "issue"
                                    let repo = $"{request.Issue.Owner}/{request.Issue.Repository}"
                                    if organization.GetProperty("id").GetString() <> binding.OrganizationId
                                       || organization.GetProperty("login").GetString() <> binding.Owner
                                       || repository.GetProperty("nameWithOwner").GetString() <> repo
                                       || native.GetProperty("id").GetString() <> request.ExpectedNodeId
                                       || native.GetProperty("number").GetInt32() <> request.Issue.Number then
                                        invalid "immutable owner or native issue identity differs"
                                    else
                                        let decode (node: JsonElement) =
                                            let itemId = node.GetProperty("id").GetString()
                                            let projectId = node.GetProperty("project").GetProperty("id").GetString()
                                            let content = node.GetProperty "content"
                                            if not (nonBlank itemId) || not (nonBlank projectId)
                                               || content.GetProperty("id").GetString() <> request.ExpectedNodeId
                                               || content.GetProperty("number").GetInt32() <> request.Issue.Number
                                               || content.GetProperty("repository").GetProperty("nameWithOwner").GetString() <> repo then
                                                invalid "membership content identity differs"
                                            else Ok(itemId, projectId, node.Clone())
                                        let connection = native.GetProperty "projectItems"
                                        if connection.GetProperty("totalCount").GetInt32() < 0 || connection.GetProperty("nodes").GetArrayLength() > 50 then
                                            invalid "membership count/window is invalid"
                                        else
                                            GraphQl.page subject "native issue project memberships" (fun (id, _, _) -> id) decode connection
                                            |> Result.map (fun page -> pages <- pages + 1; page)
                                with
                                | :? Collections.Generic.KeyNotFoundException
                                | :? InvalidOperationException -> invalid "unreadable native membership identity")
                        GraphQl.drain subject "native issue project memberships" { MaxPages = 10; MaxItems = 500 } fetch
                        |> Result.bind (fun memberships ->
                            match memberships |> List.filter (fun (_, projectId, _) -> projectId = binding.ProjectId) with
                            | [ (itemId, _, node) ] ->
                                try
                                    let select (alias: string) (field: SelectFieldBinding) =
                                        let value = node.GetProperty alias
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        else
                                            let name = value.GetProperty("name").GetString()
                                            if value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldSingleSelectValue"
                                               || value.GetProperty("field").GetProperty("id").GetString() <> field.Id
                                               || Map.tryFind name field.Options <> Some(value.GetProperty("optionId").GetString()) then
                                                invalid "single-select field/value differs from bound schema"
                                            else Ok(Some name)
                                    let roadmap =
                                        let value = node.GetProperty "roadmap"
                                        if value.ValueKind = JsonValueKind.Null then Ok None
                                        elif value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldTextValue"
                                             || value.GetProperty("field").GetProperty("id").GetString() <> binding.RoadmapFieldId then
                                            invalid "Roadmap field identity/type differs"
                                        else Ok(Some(value.GetProperty("text").GetString()))
                                    // The Work level classification rides on this membership read (#5119), with the same field and
                                    // option identity checks the former separate `selected work classification` read applied.
                                    let workLevel =
                                        match binding.WorkLevel with
                                        | None -> Ok None
                                        | Some field ->
                                            let value = node.GetProperty "workLevel"
                                            if value.ValueKind = JsonValueKind.Null then Ok(Some("Unknown", None))
                                            else
                                                let name = value.GetProperty("name").GetString()
                                                let option = value.GetProperty("optionId").GetString()
                                                if value.GetProperty("__typename").GetString() <> "ProjectV2ItemFieldSingleSelectValue"
                                                   || value.GetProperty("field").GetProperty("id").GetString() <> field.Id
                                                   || Map.tryFind name field.Options <> Some option then
                                                    invalid "Work level field/value differs from bound schema"
                                                else Ok(Some(name, Some option))
                                    select "status" binding.Status |> Result.bind (fun status -> roadmap |> Result.bind (fun roadmap ->
                                        select "track" binding.Track |> Result.bind (fun track -> select "observation" binding.Observation |> Result.bind (fun observation ->
                                            workLevel |> Result.map (fun workLevel ->
                                            { ItemId = itemId; Status = status; Roadmap = roadmap; Track = track; Observation = observation; MembershipPages = pages; WorkLevel = workLevel; Archived = node.GetProperty("isArchived").GetBoolean() })))))
                                with
                                | :? Collections.Generic.KeyNotFoundException
                                | :? InvalidOperationException -> invalid "unreadable bound planning fields"
                            | _ -> invalid "expected exactly one membership in the bound project"))))

    let readPlanning (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        if binding.BindingVersion = 5 then readSuccessorPlanning transport binding request
        else readPlanningLegacy transport binding request

    let private repositoryAllowed (binding: Binding) (issue: IssueRef) =
        let candidate = $"%s{issue.Owner}/%s{issue.Repository}"
        binding.Repositories |> Seq.exists (fun allowed -> String.Equals(allowed, candidate, StringComparison.OrdinalIgnoreCase))

    let runOneShot (verifySource: SourceVerifier) (transport: IGitHubTransport) (binding: Binding) (request: Request) =
        match validateBinding binding with
        | Error error -> Error error
        | Ok() when request.Issue.Number <= 0 || not (nonBlank request.Issue.Owner) || not (nonBlank request.Issue.Repository) || not (nonBlank request.ExpectedNodeId) ->
            invalid "the projection request" "the issue identity is incomplete"
        | Ok() when not (repositoryAllowed binding request.Issue) ->
            Error(Http(403, $"repository %s{request.Issue.Owner}/%s{request.Issue.Repository} is outside the reviewed V2 projection allowlist"))
        | Ok() when (binding.BindingVersion = 3 || isProgrammeBinding binding) && Map.tryFind request.ExpectedNodeId binding.SelectedIssues <> Some $"{request.Issue.Owner}/{request.Issue.Repository}#{request.Issue.Number}" ->
            invalid "the projection request" "the native issue is outside the selected receiver cohort"
        | Ok() ->
            match verifySource request.Issue with
            | Error error -> Error error
            | Ok(Stale last) ->
                let suffix = last |> Option.map (fun revision -> $"; last verified revision %s{revision}") |> Option.defaultValue ""
                Error(Http(409, $"source observation is stale%s{suffix}; refusing to write Verified"))
            | Ok(Refused reason) ->
                let detail = if nonBlank reason then reason else "no reason supplied"
                Error(Http(403, $"authoritative source verification refused (%s{detail}); refusing to write Verified"))
            | Ok(Current revision) when not (nonBlank revision) ->
                invalid "the source verification" "the current source revision is missing"
            | Ok(Current revision) ->
                let expected: ExactProject =
                    {
                        Owner = binding.Owner
                        Number = binding.ProjectNumber
                        Title = binding.ProjectTitle
                        Id = binding.ProjectId
                    }

                let bootstrap = verifyOrganization transport binding |> Result.bind (fun () -> bootstrapExactProject transport expected)
                match bootstrap with
                | Error error -> Error error
                | Ok board ->
                    match verifyFieldSchema binding board with
                    | Error error -> Error error
                    | Ok() ->
                        match exactMembership transport binding request.Issue request.ExpectedNodeId with
                        | Error error -> Error error
                        // An archived card (Done retention, #5119) is reported, never written: the row stays enrolled.
                        | Ok(_, _, _, true) -> Error(Http(409, archivedCardWriteRefusal))
                        | Ok(itemId, nativeId, pages, false) ->
                            match fieldValueByItemId transport binding request.Issue nativeId itemId with
                            | Error error -> Error error
                            | Ok(Some "Verified") ->
                                let current =
                                    if not (isProgrammeBinding binding) then Ok()
                                    else
                                        match verifySource request.Issue with
                                        | Ok(Current fresh) when fresh = revision -> Ok()
                                        | Error error -> Error error
                                        | Ok _ -> Error(Http(409, "source changed before Verified no-op; retaining current project value"))
                                match current with
                                | Error error -> Error error
                                | Ok() ->
                                    Ok
                                        {
                                            ProjectId = binding.ProjectId
                                            RecipeRevision = binding.RecipeRevision
                                            Issue = request.Issue
                                            ObservedRevision = revision
                                            Observation = "Verified"
                                            Outcome = AlreadyCurrent itemId
                                            SourceChecks = if isProgrammeBinding binding then 2 else 1
                                            ProjectReads = 3 + pages
                                            Mutations = 0
                                            VerifiedAt = DateTimeOffset.UtcNow
                                        }
                            | Ok(Some value) when not (binding.Observation.Options.ContainsKey value) ->
                                Error(Malformed("the Observation value", $"unknown option '%s{value}'"))
                            | Ok _ ->
                                let write =
                                    match verifySource request.Issue with
                                    | Ok(Current fresh) when fresh = revision -> setField transport board itemId "Observation" (Set "Verified")
                                    | Ok _ -> Error(Http(409, "source changed before mutation; retaining current project value"))
                                    | Error error -> Error error
                                match write with
                                | Error error -> Error error
                                | Ok() ->
                                    match fieldValueByItemId transport binding request.Issue nativeId itemId with
                                    | Ok(Some "Verified") ->
                                        Ok
                                            { ProjectId = binding.ProjectId; RecipeRevision = binding.RecipeRevision
                                              Issue = request.Issue; ObservedRevision = revision; Observation = "Verified"
                                              Outcome = Updated itemId; SourceChecks = 2; ProjectReads = 4 + pages; Mutations = 1; VerifiedAt = DateTimeOffset.UtcNow }
                                    | Ok _ -> Error(Malformed("projection readback", "owned Observation did not read back Verified"))
                                    | Error error -> Error error
