namespace FS.GG.Coord.Tests

/// Pure batched-enrollment planner controls. Synthetic identities only; every private value is a sentinel.
module V2EnrollmentTests =
    open System
    open System.Text
    open System.Text.Json.Nodes
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Enrollment

    let internal sentinel = "SYNTHETIC_PRIVATE_SENTINEL_9f310"
    let internal projectId = "PVT_kwDOEYAWY84Bldpa"
    let internal planPath = "docs/roadmaps/synthetic-plan.md"
    let internal get = function Ok value -> value | Error refusal -> failwith (refusalCode refusal)
    let internal getIo = function Ok value -> value | Error error -> failwith (Errors.explain error)

    let internal compact (node: JsonNode) = node.ToJsonString()

    /// A real predecessor pair built through the installed constructor with a fixed synthetic nonce.
    let internal predecessorPadded (count: int) (trailing: string) (pad: int) =
        let padding = String.replicate pad "p"
        let rows = [ for i in 0 .. count - 1 -> $"FS-GG/.github#{70001 + i}", $"I_synthetic_{sentinel}_{i}", $"https://github.com/FS-GG/.github/blob/main/docs/roadmaps/old-{i}{padding}.md" ]
        let field (label: string) (options: string list) =
            let node = JsonObject()
            node["id"] <- JsonValue.Create("synthetic-" + label)
            let map = JsonObject()
            options |> List.iteri (fun index name -> map[name] <- JsonValue.Create $"synthetic-{label}-{index}")
            node["options"] <- map
            node
        let template = JsonObject()
        let set (name: string) (value: obj) = template[name] <- JsonValue.Create value
        set "bindingVersion" 5; set "schemaVersion" 3; set "importRecipeRevision" (String.replicate 40 "a"); set "importArtifactSha256" (String.replicate 64 "b")
        let selected = JsonObject()
        for issue, node, _ in rows do selected[node] <- JsonValue.Create issue
        template["selectedIssues"] <- selected
        set "recipeRevision" (String.replicate 40 "c"); set "populationRevision" (String.replicate 40 "e"); set "organizationId" "O_kgDOEYAWYw"; set "artifactSha256" (String.replicate 64 "d")
        set "ownerKind" "organization"; set "owner" "FS-GG"; set "projectNumber" 3; set "projectTitle" "Coordination V2"; set "projectId" projectId
        template["status"] <- field "status" [ "Backlog"; "Ready"; "In progress"; "In review"; "Blocked"; "Done" ]
        set "roadmapFieldId" "synthetic-roadmap"
        template["track"] <- field "track" [ "Active delivery"; "Follow-up" ]
        template["observation"] <- field "observation" [ "Verified"; "Stale"; "Unknown" ]
        template["workLevel"] <- field "work-level" [ "Outcome"; "Delivery" ]
        let repositories = JsonArray(JsonValue.Create "FS-GG/.github" :> JsonNode, JsonValue.Create "FS-GG/FS.GG.Templates" :> JsonNode)
        template["repositories"] <- repositories
        let recipe, artifact = String.replicate 40 "a", String.replicate 64 "b"
        let payload =
            let items = rows |> List.map (fun (issue, node, roadmap) -> $"{{\"issue\":\"{issue}\",\"nodeId\":\"{node}\",\"roadmap\":\"{roadmap}\"}}") |> String.concat ","
            $"{{\"schema\":\"fsgg.coordination-board-v2-enrollment-payload/v1\",\"target\":{{\"owner\":\"FS-GG\",\"organizationId\":\"O_kgDOEYAWYw\",\"projectId\":\"{projectId}\",\"projectNumber\":3,\"schemaVersion\":3}},\"binding\":{{\"recipeRevision\":\"{recipe}\",\"artifactSha256\":\"{artifact}\",\"repositories\":[\"FS-GG/.github\",\"FS-GG/FS.GG.Templates\"]}},\"items\":[{items}]}}{trailing}"
            |> Encoding.UTF8.GetBytes
        let privateJson, publicJson = V2Projection.prepareProgrammeBinding (compact template) payload (fun () -> Array.create 32 7uy) |> getIo
        privateJson, publicJson, payload

    let internal predecessor (count: int) (trailing: string) = predecessorPadded count trailing 0

    let internal mergeSha = String.replicate 40 "9"

    let internal request =
        { Cohort = "cohort-synthetic"; PredecessorPrivatePath = "/synthetic/private.json"; PredecessorMergeSha = mergeSha
          Issues =
            [ { Ref = "FS-GG/.github#80001"; Slice = "synthetic-outcome"; Plan = ProgrammeIssue3008; Classification = Outcome }
              { Ref = "FS-GG/FS.GG.Templates#80002"; Slice = "synthetic-delivery"; Plan = ProtectedDoc("FS-GG/.github", planPath); Classification = Delivery } ] }

    let internal fact (reference: string) (node: string) : NativeIssueFact =
        { Ref = reference; NodeId = node; Kind = NativeIssue; Repository = reference.Substring(0, reference.IndexOf '#')
          ProjectMember = true; Complete = true; HumanRoadmap = None }

    let internal facts : NativeFacts =
        { ProjectId = projectId
          Issues = [ fact "FS-GG/.github#80001" "I_synthetic_new1"; fact "FS-GG/FS.GG.Templates#80002" "I_synthetic_new2" ]
          PlanBlobs = [ { Repository = "FS-GG/.github"; Path = planPath; OnMain = true } ] }

    let private planned = lazy (let privateJson, publicJson, _ = predecessor 5 "" in plan privateJson publicJson request facts)

    [<Fact>]
    let ``the planned payload is the predecessor bytes minus the closing, the spliced rows, and the closing`` () =
        let privateJson, publicJson, original = predecessor 5 ""
        let result = plan privateJson publicJson request facts |> get
        let fragment = "{\"issue\":\"FS-GG/.github#80001\",\"nodeId\":\"I_synthetic_new1\",\"roadmap\":\"https://github.com/FS-GG/.github/issues/3008\"},{\"issue\":\"FS-GG/FS.GG.Templates#80002\",\"nodeId\":\"I_synthetic_new2\",\"roadmap\":\"https://github.com/FS-GG/.github/blob/main/" + planPath + "\"}"
        let expected = Array.concat [ original.[0 .. original.Length - 3]; Encoding.UTF8.GetBytes ("," + fragment + "]}") ]
        Assert.Equal<byte array>(expected, result.Payload)
        Assert.Equal(5, result.PredecessorRows)
        Assert.Equal(7, result.TotalRows)
        Assert.Equal<string list>([ Outcome; Delivery ] |> List.map string, result.Appended |> List.map (fun row -> string row.Classification))
        // The installed constructor accepts the template and payload; the planner and constructor agree.
        Assert.True(V2Projection.prepareProgrammeBinding result.TemplateJson result.Payload (fun () -> Array.create 32 1uy) |> Result.isOk)

    [<Fact>]
    let ``the Outcome issue URL used as a Roadmap in cohort 89 r1 is not admitted by the shared installed grammar`` () =
        let repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/FS.GG.Game" ]
        let bogus = "https://github.com/FS-GG/.github/issues/4930"
        Assert.True(V2ProjectionSource.programmePlanBlobGrammar repositories bogus |> Result.isError)
        Assert.NotEqual<string>(bogus, V2ProjectionSource.programmePlanIssueUrl)
        Assert.Equal("https://github.com/FS-GG/.github/issues/3008", V2ProjectionSource.programmePlanIssueUrl)
        for admitted in [ "https://github.com/FS-GG/.github/blob/main/docs/roadmaps/game-render-multi-01.md"
                          "https://github.com/FS-GG/.github/blob/" + String.replicate 40 "a" + "/docs/x.md" ] do
            Assert.True(V2ProjectionSource.programmePlanBlobGrammar repositories admitted |> Result.isOk, admitted)
        for refused in [ "https://github.com/FS-GG/.github/blob/main/docs/a:b.md"; "https://github.com/FS-GG/.github/blob/main/docs/../x.md"
                         "https://github.com/FS-GG/.github/blob/main/roadmaps/x.md"; "https://github.com/Other/Repo/blob/main/docs/x.md"
                         "http://github.com/FS-GG/.github/blob/main/docs/x.md"; "https://github.com/FS-GG/.github/blob/main/docs/x%2Fy.md" ] do
            Assert.True(V2ProjectionSource.programmePlanBlobGrammar repositories refused |> Result.isError, refused)

    let private outcomePlan = { request with Issues = [ { request.Issues.[1] with Plan = ProtectedDoc("FS-GG/.github", "issues/4930") } ] }
    let private outcomeFacts = { facts with PlanBlobs = [ { Repository = "FS-GG/.github"; Path = "issues/4930"; OnMain = true } ]; Issues = [ facts.Issues.[1] ] }

    [<Fact>]
    let ``causal control: the grammar check alone refuses the Outcome-URL plan and removing it lets construction proceed`` () =
        let privateJson, publicJson, _ = predecessor 5 ""
        // With the real grammar the bogus plan refuses before any construction.
        Assert.Equal(Error PlanNotAdmitted, plan privateJson publicJson outcomePlan outcomeFacts |> Result.map ignore)
        // Replace only the grammar stage with an accept-all stage: the same input is now constructed. The refusal is caused by the grammar.
        let permissive = { defaultSeams with Grammar = fun _ url -> Ok("FS-GG/.github", "main", url.Substring(url.IndexOf "/blob/main/" + "/blob/main/".Length)) }
        match planWith permissive privateJson publicJson outcomePlan outcomeFacts with
        | Ok built -> Assert.Equal(6, built.TotalRows)
        | Error refusal -> failwithf "grammar removal did not change the outcome: %s" (refusalCode refusal)

    [<Fact>]
    let ``prefix-mutation controls: a mutated splice is refused at any prefix position, header or row`` () =
        let privateJson, publicJson, original = predecessor 5 ""
        let prefixEnd = original.Length - 2
        let positions = [ 0; 20; Encoding.UTF8.GetString(original).IndexOf "\"items\":[" ; prefixEnd / 2; prefixEnd - 3; prefixEnd - 1 ]
        for position in positions do
            let mutate = { defaultSeams with Splice = fun predecessor rows ->
                                                    let spliced = defaultSeams.Splice predecessor rows
                                                    spliced.[position] <- (if spliced.[position] = byte 'Q' then byte 'R' else byte 'Q')
                                                    spliced }
            Assert.Equal(Error PrefixNotPreserved, planWith mutate privateJson publicJson request facts |> Result.map ignore)
        // A splice that drops or reorders the separator is refused as well, and the unmutated splice is accepted.
        let noComma = { defaultSeams with Splice = fun predecessor rows -> Array.concat [ predecessor.[0 .. predecessor.Length - 3]; rows; [| byte ']'; byte '}' |] ] }
        Assert.Equal(Error PrefixNotPreserved, planWith noComma privateJson publicJson request facts |> Result.map ignore)
        Assert.True(planWith defaultSeams privateJson publicJson request facts |> Result.isOk)

    [<Fact>]
    let ``verifyPair reports each preservation boolean independently`` () =
        let privateJson, _, _ = predecessor 5 ""
        let result = planned.Value |> get
        let successorPrivate, successorPublic = V2Projection.prepareProgrammeBinding result.TemplateJson result.Payload (fun () -> Array.create 32 3uy) |> getIo
        let verdict = verifyPair privateJson successorPrivate successorPublic
        Assert.Equal({ PrefixPreserved = true; HeaderPreserved = true; RecomputedCommitment = true; PredecessorRows = 5; AppendedRows = 2; TotalRows = 7 }, verdict)
        // A different nonce makes the public commitment stale while prefix and header remain intact.
        let _, otherPublic = V2Projection.prepareProgrammeBinding result.TemplateJson result.Payload (fun () -> Array.create 32 4uy) |> getIo
        let stale = verifyPair privateJson successorPrivate otherPublic
        Assert.Equal((true, true, false), (stale.PrefixPreserved, stale.HeaderPreserved, stale.RecomputedCommitment))
        // A payload whose old row changed but whose own commitment is recomputed is caught only by the prefix check.
        let tampered = Array.copy result.Payload
        let at = Encoding.UTF8.GetString(tampered).IndexOf "old-1" + 4
        tampered.[at] <- byte '2'
        let tamperedPrivate, tamperedPublic = V2Projection.prepareProgrammeBinding result.TemplateJson tampered (fun () -> Array.create 32 5uy) |> getIo
        let mutated = verifyPair privateJson tamperedPrivate tamperedPublic
        Assert.Equal((false, true, true), (mutated.PrefixPreserved, mutated.HeaderPreserved, mutated.RecomputedCommitment))
        // Garbage never throws and never claims preservation.
        Assert.Equal(false, (verifyPair "{" successorPrivate successorPublic).PrefixPreserved)

    [<Fact>]
    let ``the planner row ceiling is the installed reader ceiling`` () =
        Assert.Equal(100, rowCap)
        let privateJson, publicJson, _ = predecessor 98 ""
        Assert.True(plan privateJson publicJson request facts |> Result.isOk)
        let installed = plan privateJson publicJson request facts |> get
        Assert.Equal(100, installed.TotalRows)
        // The installed constructor itself refuses a 101-row payload, so the ceiling cannot drift silently.
        let tooMany = { request with Issues = request.Issues @ [ { request.Issues.[0] with Ref = "FS-GG/.github#80003" } ] }
        let extraFacts = { facts with Issues = facts.Issues @ [ fact "FS-GG/.github#80003" "I_synthetic_new3" ] }
        Assert.Equal(Error RowCapExceeded, plan privateJson publicJson tooMany extraFacts |> Result.map ignore)
        let result = plan privateJson publicJson request facts |> get
        let extraRow = ",{\"issue\":\"FS-GG/.github#80003\",\"nodeId\":\"I_synthetic_new3\",\"roadmap\":\"https://github.com/FS-GG/.github/issues/3008\"}"
        let beyond = Array.concat [ result.Payload.[0 .. result.Payload.Length - 3]; Encoding.UTF8.GetBytes extraRow; [| byte ']'; byte '}' |] ]
        let template = JsonNode.Parse result.TemplateJson
        template["selectedIssues"].AsObject().Add("I_synthetic_new3", JsonValue.Create "FS-GG/.github#80003")
        Assert.True(V2Projection.prepareProgrammeBinding (compact template) beyond (fun () -> Array.create 32 1uy) |> Result.isError)

    [<Fact>]
    let ``refusal codes are the closed printable case names and carry no input`` () =
        let all = [ DuplicateIssue; AlreadyEnrolled; IsPullRequest; NodeIdMismatch; NotProjectMember; RepositoryOutsideBinding; PredecessorCommitmentMismatch
                    PrefixNotPreserved; PlanNotAdmitted; PlanBlobAbsentOnMain; RowCapExceeded; ConflictingHumanValue; PartialVisibility; OutputExists; MalformedInput
                    InputUnreadable; CustodyUnavailable; GitReadFailed; NativeReadFailed; SuccessorNotRederived ]
        Assert.Equal(20, (all |> List.map refusalCode |> Set.ofList).Count)
        for refusal in all do Assert.Equal(string refusal, refusalCode refusal)

    [<Fact>]
    let ``requests and facts are strict closed documents`` () =
        let valid = "{\"schema\":\"fsgg.board-v2-successor-request/1\",\"cohort\":\"c1\",\"predecessor\":{\"privatePath\":\"/p\",\"mergeSha\":\"" + mergeSha + "\"},\"issues\":[{\"ref\":\"FS-GG/.github#1\",\"slice\":\"s\",\"plan\":{\"kind\":\"programmeIssue3008\"},\"classification\":\"Outcome\"}]}"
        Assert.True(decodeRequest valid |> Result.isOk)
        for bad in [ valid.Replace("\"cohort\":\"c1\"", "\"cohort\":\"c1\",\"cohort\":\"c2\"")
                     valid.Replace("\"slice\":\"s\"", "\"slice\":\"s\",\"extra\":" + "\"" + sentinel + "\"")
                     valid.Replace("programmeIssue3008", "other")
                     valid.Replace("Outcome", "Roadmap")
                     valid.Replace("#1", "#01")
                     valid.Replace(mergeSha, "ABC")
                     "﻿" + valid
                     "" ] do
            Assert.Equal(Error MalformedInput, decodeRequest bad |> Result.map ignore)
        Assert.Equal(Error MalformedInput, decodeNativeFacts "{\"schema\":\"fsgg.board-v2-native-facts/1\"}" |> Result.map ignore)
        // A refusal value never carries the rejected text.
        Assert.DoesNotContain(sentinel, sprintf "%A" (decodeRequest (valid.Replace("\"slice\":\"s\"", "\"slice\":\"" + sentinel + "\""))))

    [<Fact>]
    let ``non-spliceable predecessor payloads are refused rather than rewritten`` () =
        let privateJson, publicJson, _ = predecessor 5 "\n"
        Assert.Equal(Error PrefixNotPreserved, plan privateJson publicJson request facts |> Result.map ignore)

    [<Fact>]
    let ``every refusal code is reachable from the pure planner`` () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let run r f = plan privateJson publicJson r f |> Result.map ignore
        let withIssue0 (changes: NativeIssueFact -> NativeIssueFact) = { facts with Issues = changes facts.Issues.[0] :: facts.Issues.Tail }
        Assert.Equal(Error DuplicateIssue, run { request with Issues = request.Issues @ [ request.Issues.[0] ] } facts)
        Assert.Equal(Error IsPullRequest, run request (withIssue0 (fun f -> { f with Kind = NativePullRequest })))
        Assert.Equal(Error NotProjectMember, run request (withIssue0 (fun f -> { f with ProjectMember = false })))
        Assert.Equal(Error PartialVisibility, run request (withIssue0 (fun f -> { f with Complete = false })))
        Assert.Equal(Error NodeIdMismatch, run request (withIssue0 (fun f -> { f with NodeId = "I_synthetic_" + sentinel + "_1" })))
        Assert.Equal(Error ConflictingHumanValue, run request (withIssue0 (fun f -> { f with HumanRoadmap = Some "https://github.com/FS-GG/.github/issues/4930" })))
        Assert.True(run request (withIssue0 (fun f -> { f with HumanRoadmap = Some V2ProjectionSource.programmePlanIssueUrl })) |> Result.isOk)
        Assert.Equal(Error PlanBlobAbsentOnMain, run request { facts with PlanBlobs = [] })
        Assert.Equal(Error RepositoryOutsideBinding, run { request with Issues = [ { request.Issues.[0] with Ref = "FS-GG/Other#9" } ] } facts)
        Assert.Equal(Error PlanNotAdmitted, plan privateJson publicJson outcomePlan outcomeFacts |> Result.map ignore)
        let enrolled = { request with Issues = [ { request.Issues.[0] with Ref = "FS-GG/.github#70001" } ] }
        Assert.Equal(Error AlreadyEnrolled, run enrolled { facts with Issues = [ fact "FS-GG/.github#70001" ("I_synthetic_" + sentinel + "_0") ] })
        Assert.Equal(Error PredecessorCommitmentMismatch, plan privateJson (publicJson.Replace(publicJson.Substring(publicJson.Length - 10, 4), "0000")) request facts |> Result.map ignore)

    // ---- #4941 PR 2: analysis, O1 (re-derivation), O4 (escaping and redaction) ----

    [<Fact>]
    let ``analyze reports every independent finding and its head is exactly the refusal the planner returns`` () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let ref0, ref1 = request.Issues.[0].Ref, request.Issues.[1].Ref
        let factMutations : (string * (NativeFacts -> NativeFacts)) list =
            let issue0 change (facts: NativeFacts) = { facts with Issues = change facts.Issues.[0] :: facts.Issues.Tail }
            let issue1 change (facts: NativeFacts) = { facts with Issues = facts.Issues.Head :: [ change facts.Issues.[1] ] }
            [ "pr", issue0 (fun f -> { f with Kind = NativePullRequest })
              "member", issue1 (fun f -> { f with ProjectMember = false })
              "partial", issue1 (fun f -> { f with Complete = false })
              "node", issue0 (fun f -> { f with NodeId = "X_bad" })
              "blob", (fun facts -> { facts with PlanBlobs = [] })
              "human", issue0 (fun f -> { f with HumanRoadmap = Some "https://github.com/FS-GG/.github/issues/4930" })
              "project", (fun facts -> { facts with ProjectId = "PVT_other" })
              "enrolled", issue0 (fun f -> { f with Ref = "FS-GG/.github#70001"; NodeId = "I_synthetic_" + sentinel + "_0" }) ]
        let requestMutations : (string * (Request -> Request)) list =
            [ "duplicate", (fun r -> { r with Issues = r.Issues @ [ r.Issues.[0] ] })
              "outside", (fun r -> { r with Issues = { r.Issues.[0] with Ref = "FS-GG/Other#9" } :: r.Issues.Tail })
              "grammar", (fun r -> { r with Issues = r.Issues.Head :: [ { r.Issues.[1] with Plan = ProtectedDoc("FS-GG/.github", "issues/4930") } ] }) ]
        let scenarios =
            [ for n1, f1 in factMutations do
                yield n1, id, f1
                for n2, f2 in factMutations do yield n1 + "+" + n2, id, (f1 >> f2)
              for n1, r1 in requestMutations do
                yield n1, r1, id
                for n2, f2 in factMutations do yield n1 + "+" + n2, r1, f2 ]
        let mutable multi = 0
        for name, r, f in scenarios do
            let r', f' = r request, f facts
            let analysis = analyze privateJson publicJson r' f' |> get
            match plan privateJson publicJson r' f', analysis.Findings with
            | Ok _, [] -> ()
            | Error refusal, head :: rest ->
                if not rest.IsEmpty && rest |> List.exists (fun x -> x.Refusal <> head.Refusal) then multi <- multi + 1
                Assert.True((refusal = head.Refusal), name)
            | Ok _, findings -> failwithf "%s: plan succeeded with %d findings" name findings.Length
            | Error refusal, [] -> failwithf "%s: plan refused %s with no finding" name (refusalCode refusal)
        // The report is genuinely multi-finding, not first-only.
        Assert.True(multi > 20, string multi)
        Assert.True((analyze privateJson publicJson request facts |> get).Findings.IsEmpty)
        ignore (ref0, ref1)

    [<Fact>]
    let ``row fragment values are JSON-escaped; the unescaped concatenation is not valid JSON`` () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let hostile = "I_a\"b\\c,\"roadmap\":\"xé\u0001"
        let hostileFacts = { facts with Issues = { facts.Issues.[0] with NodeId = hostile } :: facts.Issues.Tail }
        let mutable captured : byte array = [||]
        let capture = { defaultSeams with Splice = fun predecessor rows -> captured <- rows; defaultSeams.Splice predecessor rows }
        planWith capture privateJson publicJson request hostileFacts |> ignore
        Assert.True(captured.Length > 0)
        use document = System.Text.Json.JsonDocument.Parse("[" + Encoding.UTF8.GetString captured + "]")
        let rows = document.RootElement.EnumerateArray() |> Seq.toList
        Assert.Equal(2, rows.Length)
        Assert.Equal(hostile, rows.[0].GetProperty("nodeId").GetString())
        Assert.Equal("https://github.com/FS-GG/.github/issues/3008", rows.[0].GetProperty("roadmap").GetString())
        // Causal leg: the PR 1 style concatenation of the same value cannot be parsed, so escaping is what makes it valid.
        let naive = "[{\"issue\":\"x\",\"nodeId\":\"" + hostile + "\",\"roadmap\":\"y\"}]"
        Assert.ThrowsAny<exn>(fun () -> System.Text.Json.JsonDocument.Parse(naive) |> ignore) |> ignore

    [<Fact>]
    let ``a successor plan never prints its template or payload through generic printing`` () =
        let planned = planned.Value |> get
        // Positive leg: the sentinel really is in the private bytes, so its absence below means something.
        Assert.Contains(sentinel, Encoding.UTF8.GetString planned.Payload)
        Assert.Contains(sentinel, planned.TemplateJson)
        for printed in [ sprintf "%A" planned; sprintf "%A" (Ok planned: Result<SuccessorPlan, Refusal>); sprintf "%O" planned; string planned; $"{planned}"; planned.ToString() ] do
            Assert.DoesNotContain(sentinel, printed)
            Assert.DoesNotContain("I_synthetic", printed)
            Assert.DoesNotContain(Convert.ToBase64String planned.Payload, printed)
            Assert.DoesNotContain("populationRevision", printed)
            Assert.Contains("content withheld", printed)

    let private prepared () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let built = plan privateJson publicJson request facts |> get
        privateJson, publicJson, built

    let private accepted (built: SuccessorPlan) (template: string) (payload: byte array) =
        V2Projection.prepareProgrammeBinding template payload (fun () -> Array.create 32 2uy) |> Result.isOk

    [<Fact>]
    let ``O1 causal controls: hand-edited successor inputs reach a commitment without the re-derivation and are refused with it`` () =
        let privateJson, publicJson, built = prepared ()
        let verify template payload = verifyPrepared privateJson publicJson request facts template payload
        Assert.Equal(Ok(), verify built.TemplateJson built.Payload)
        let text = Encoding.UTF8.GetString built.Payload
        let swap (oldText: string) (newText: string) =
            Assert.Contains(oldText, text)
            Encoding.UTF8.GetBytes(text.Replace(oldText, newText))
        // 1. The cohort 89 r1 failure shape: an appended Roadmap that is an Outcome issue URL.
        let bogus = swap "\"roadmap\":\"https://github.com/FS-GG/.github/issues/3008\"" "\"roadmap\":\"https://github.com/FS-GG/.github/issues/4930\""
        Assert.True(accepted built built.TemplateJson bogus, "without the re-derivation the constructor accepts the edited payload")
        Assert.Equal(Error PlanNotAdmitted, verify built.TemplateJson bogus)
        // 2. A consistently swapped node id (payload and template together) that the facts do not support.
        let newNode = "I_synthetic_new1"
        let swappedPayload = swap newNode "I_synthetic_other"
        let swappedTemplate = built.TemplateJson.Replace(newNode, "I_synthetic_other")
        Assert.True(accepted built swappedTemplate swappedPayload)
        Assert.Equal(Error NodeIdMismatch, verify swappedTemplate swappedPayload)
        // 3. A semantically equal re-spacing of the appended rows is still not the derived payload.
        let spaced = swap "},{\"issue\":\"FS-GG/FS.GG.Templates#80002\"" "}, {\"issue\":\"FS-GG/FS.GG.Templates#80002\""
        Assert.True(accepted built built.TemplateJson spaced)
        Assert.Equal(Error SuccessorNotRederived, verify built.TemplateJson spaced)
        // 4. A template that drifts any non-derived field.
        let drifted = built.TemplateJson.Replace("synthetic-status-1", "synthetic-status-drifted")
        Assert.NotEqual<string>(built.TemplateJson, drifted)
        Assert.True(accepted built drifted built.Payload)
        Assert.Equal(Error SuccessorNotRederived, verify drifted built.Payload)
        // 5. A mutated prefix byte (an old row's roadmap).
        let mutated = Array.copy built.Payload
        mutated.[text.IndexOf "old-1" + 4] <- byte '2'
        Assert.True(accepted built built.TemplateJson mutated)
        Assert.Equal(Error PrefixNotPreserved, verify built.TemplateJson mutated)

    [<Fact>]
    let ``O1 refuses with the planner's own refusal when the facts or request no longer support the inputs`` () =
        let privateJson, publicJson, built = prepared ()
        let stale = { facts with PlanBlobs = [] }
        Assert.Equal(Error PlanBlobAbsentOnMain, verifyPrepared privateJson publicJson request stale built.TemplateJson built.Payload)

    // ---- O2: exclusive creation maps a lost CreateNew race to OutputExists ----

    [<Fact>]
    let ``createExclusive: an existing entry at create time is OutputExists, anything else is CustodyUnavailable`` () =
        let directory = IO.Path.Combine(IO.Path.GetTempPath(), "v2custody-" + Guid.NewGuid().ToString("N"))
        IO.Directory.CreateDirectory directory |> ignore
        try
            let path = IO.Path.Combine(directory, "out.json")
            // First create succeeds, owner-only.
            use first = (match V2Custody.createExclusive path with Ok stream -> stream | Error refusal -> failwith (refusalCode refusal))
            first.Write(ReadOnlySpan<byte>(Encoding.UTF8.GetBytes sentinel))
            first.Flush true
            if not (OperatingSystem.IsWindows()) then
                Assert.Equal(IO.UnixFileMode.UserRead ||| IO.UnixFileMode.UserWrite, IO.File.GetUnixFileMode path)
            // The race: no earlier absence check is made here, so the create itself is what meets the existing entry.
            Assert.Equal(Error OutputExists, V2Custody.createExclusive path |> Result.map ignore)
            // Control: the original bytes are untouched by the lost race.
            first.Dispose()
            Assert.Equal(sentinel, IO.File.ReadAllText path)
            // A directory in the way is also an existing entry.
            IO.Directory.CreateDirectory(IO.Path.Combine(directory, "dir.json")) |> ignore
            Assert.Equal(Error OutputExists, V2Custody.createExclusive (IO.Path.Combine(directory, "dir.json")) |> Result.map ignore)
            // Not an existing entry: a missing parent is custody, not a race.
            Assert.Equal(Error CustodyUnavailable, V2Custody.createExclusive (IO.Path.Combine(directory, "missing", "out.json")) |> Result.map ignore)
        finally
            IO.Directory.Delete(directory, true)
