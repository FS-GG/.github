namespace FS.GG.Coord

module ContextView =
    open System
    open System.IO
    open System.Text
    open System.Text.Json
    open System.Text.Encodings.Web
    open System.Text.RegularExpressions
    open System.Security.Cryptography
    open FS.GG.Coord.Types

    [<Literal>]
    let Schema = "fsgg.coord.context-view/1"

    // Chosen against measured real FS-GG issues (see the #4940 PR body): the mandatory content of the largest
    // measured owning issue (.github#4753) plus the pull request, navigation and gap overhead is below 9 KiB,
    // so 12 KiB leaves headroom without letting a 32 KiB Acceptance evidence history in.
    let DefaultCapBytes = 12288
    let MinimumCapBytes = 4096
    let MaximumCapBytes = 262144

    type Action =
        | Implement
        | Repair
        | Review
        | Merge
        | Accept
        | Resume

    let actionName action =
        match action with
        | Implement -> "implement"
        | Repair -> "repair"
        | Review -> "review"
        | Merge -> "merge"
        | Accept -> "accept"
        | Resume -> "resume"

    let parseAction (text: string) =
        match text with
        | "implement" -> Ok Implement
        | "repair" -> Ok Repair
        | "review" -> Ok Review
        | "merge" -> Ok Merge
        | "accept" -> Ok Accept
        | "resume" -> Ok Resume
        | _ -> Error "action must be one of implement|repair|review|merge|accept|resume"

    type IssueRef = { Repository: string; Number: int }

    let private issueRefPattern = Regex(@"^([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)#([1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)

    let parseIssueRef (text: string) =
        let m = issueRefPattern.Match(if isNull text then "" else text)
        if m.Success then Ok { Repository = m.Groups[1].Value; Number = int m.Groups[2].Value }
        else Error "issue must be OWNER/REPO#N"

    type Read<'a> =
        | Supplied of 'a
        | Unavailable of reason: string
        | NotRequested

    type IssueFact = { Title: string; State: string; UpdatedAt: string; Body: string }

    type CheckFact = { Name: string; Status: string; Conclusion: string option }

    type CheckPopulation = { Checks: CheckFact list; Complete: bool }

    type PullRequestFact =
        { Number: int
          Url: string
          State: string
          Draft: bool
          Merged: bool
          Head: string
          HeadRef: string
          BaseRef: string
          ChangedFiles: int
          Body: string
          Checks: Read<CheckPopulation> }

    type CustodyFact =
        { Issue: string
          Slice: string
          Attempt: string
          Owner: string
          Repository: string
          PullRequest: int
          Head: string
          Unknowns: string list
          Terminal: string option
          Gap: string option }

    type FileFact =
        | File of sha256: string
        | Directory
        | Absent
        | Unreadable of reason: string

    type AtlasEntry = { Id: string; Kind: string; Status: string; Title: string; Record: string }

    type NavigationFacts =
        { Files: Map<string, FileFact>
          Atlas: Read<AtlasEntry list> }

    type Observations =
        { Issue: IssueRef
          Action: Action
          RequestedPullRequest: int option
          CapBytes: int
          IssueRead: Read<IssueFact>
          PullRequestRead: Read<PullRequestFact>
          Custody: Read<CustodyFact>
          Navigation: Read<NavigationFacts> }

    type GapSeverity =
        | Obligation
        | Context

    type Gap = { Code: string; Severity: GapSeverity; Subject: string; Detail: string }

    type Omission = { Section: string; Mandatory: bool; Bytes: int; Sha256: string; Retrieval: string }

    type Status =
        | Complete
        | Incomplete

    type View =
        { Status: Status
          Json: string
          Bytes: int
          Sha256: string
          Gaps: Gap list
          Omissions: Omission list }

    type Refusal = CapOutOfBounds of cap: int * minimumNeeded: int

    // ---------------------------------------------------------------- JSON (deterministic, closed vocabulary)

    type private J =
        | JStr of string
        | JNum of int64
        | JBool of bool
        | JNull
        | JArr of J list
        | JObj of (string * J) list

    let private encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping

    let rec private write (w: Utf8JsonWriter) (value: J) =
        match value with
        | JStr s -> w.WriteStringValue s
        | JNum n -> w.WriteNumberValue n
        | JBool b -> w.WriteBooleanValue b
        | JNull -> w.WriteNullValue()
        | JArr items ->
            w.WriteStartArray()
            items |> List.iter (write w)
            w.WriteEndArray()
        | JObj fields ->
            w.WriteStartObject()
            for name, v in fields do
                w.WritePropertyName name
                write w v
            w.WriteEndObject()

    let private render (value: J) =
        use stream = new MemoryStream()
        use w = new Utf8JsonWriter(stream, JsonWriterOptions(Encoder = encoder, Indented = false))
        write w value
        w.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    let private bytesOf (s: string) = Encoding.UTF8.GetByteCount s

    let private sha256 (s: string) =
        SHA256.HashData(Encoding.UTF8.GetBytes s) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private optStr (value: string option) =
        match value with
        | Some s -> JStr s
        | None -> JNull

    // ---------------------------------------------------------------- six-field parse

    let private fieldNames = [ "Owner"; "Done when"; "Current deliverable"; "Next action"; "Blocked on"; "Acceptance evidence" ]

    let private gap code severity subject detail =
        { Code = code; Severity = severity; Subject = subject; Detail = detail }

    let private severityOf name =
        if name = "Owner" || name = "Done when" then Obligation else Context

    let parseSections (body: string) =
        let classified = Markdown.classify body |> List.toArray
        // Headings count only outside fenced code: a quoted copy of the contract never supplies a field.
        let headings =
            classified
            |> Array.indexed
            |> Array.choose (fun (index, (line, kind)) ->
                let trimmed = line.Trim()
                if kind = Markdown.Text && trimmed.StartsWith("## ", StringComparison.Ordinal) then
                    Some(index, trimmed.Substring(3).Trim())
                else None)
        let sectionText index =
            let start, _ = headings[index]
            let finish = if index + 1 < headings.Length then fst headings[index + 1] else classified.Length
            // Verbatim between headings. Fenced lines inside the section are part of it; only surrounding blanks go.
            classified[start + 1 .. finish - 1] |> Array.map fst |> String.concat "\n" |> _.Trim()
        let occurrences name = headings |> Array.indexed |> Array.filter (fun (_, (_, n)) -> n = name) |> Array.map fst
        let mutable values = Map.empty
        let gaps = ResizeArray<Gap>()
        for name in fieldNames do
            match occurrences name with
            | [||] -> gaps.Add(gap "missing-section" (severityOf name) name $"the issue body has no '## {name}' section outside fenced code")
            | [| index |] ->
                let text = sectionText index
                if String.IsNullOrWhiteSpace text then
                    gaps.Add(gap "empty-section" (severityOf name) name $"'## {name}' is present but empty")
                else values <- Map.add name text values
            | many ->
                gaps.Add(gap "duplicate-section" (severityOf name) name $"'## {name}' occurs {many.Length} times outside fenced code; no copy is chosen for the author")
        for _, name in headings do
            if not (List.contains name fieldNames) then
                gaps.Add(gap "unrecognised-section" Context name $"'## {name}' is not a six-field section and is not carried")
        values, List.ofSeq gaps |> List.distinct

    // ---------------------------------------------------------------- touch-set, instructions, requested paths

    let instructionPaths action =
        let common = [ "AGENTS.md"; ".agents/skills/work-board/SKILL.md" ]
        match action with
        | Review -> common
        | _ -> common @ [ ".agents/skills/pnext-item/SKILL.md" ]

    let private isPattern (path: string) = path.IndexOfAny [| '*'; '?'; '[' |] >= 0

    let private touchTokens (body: string) =
        match TouchSet.parse body with
        | Declared tokens -> tokens
        | _ -> []

    let requestedPaths action (issue: IssueFact) =
        let touched =
            touchTokens issue.Body
            |> List.choose (function
                | Matchable path when not (isPattern path) -> Some(path.TrimEnd('/'))
                | _ -> None)
        (instructionPaths action @ touched) |> List.distinct |> List.truncate 64

    // ---------------------------------------------------------------- pure helpers

    let private urlPattern =
        Regex(@"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/(?:issues|pull)/[0-9]+(?:#[A-Za-z0-9_-]+)?", RegexOptions.CultureInvariant)

    let private refPattern = Regex(@"(?<![A-Za-z0-9_/#])((?:[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)?#[1-9][0-9]{0,8})\b", RegexOptions.CultureInvariant)

    let private distinctMatches (pattern: Regex) (group: int) (text: string) =
        pattern.Matches text |> Seq.map (fun m -> m.Groups[group].Value) |> Seq.distinct |> Seq.truncate 32 |> List.ofSeq

    let private markerPattern =
        Regex(@"<!--\s*fsgg:routine-development/v1\s+head=([0-9a-f]{40})(?:\s[^>]*)?-->", RegexOptions.CultureInvariant)

    let private atlasPattern =
        Regex(@"(?<![A-Za-z0-9_./:#-])([a-z][a-z0-9-]*(?:/[a-z0-9][a-z0-9._-]*)+)", RegexOptions.CultureInvariant)

    let private successLike (check: CheckFact) =
        check.Status = "completed"
        && (match check.Conclusion with
            | Some "success" | Some "neutral" | Some "skipped" -> true
            | _ -> false)

    let private pending (check: CheckFact) = check.Status <> "completed"
    let private failing (check: CheckFact) = not (successLike check) && not (pending check)

    // ---------------------------------------------------------------- sections

    type private Section =
        { Key: string
          Priority: int
          Mandatory: bool
          Value: J
          Retrieval: string }

    let private pullRequestRequired action =
        match action with
        | Implement | Resume -> false
        | Repair | Review | Merge | Accept -> true

    let private issueRetrieval (issue: IssueRef) heading =
        $"gh api repos/{issue.Repository}/issues/{issue.Number} --jq .body  # section: ## {heading}"

    let private markerState (pr: PullRequestFact) =
        let found = markerPattern.Matches pr.Body |> Seq.map (fun m -> m.Groups[1].Value) |> Seq.toList
        match found with
        | [] -> "absent", JNull
        | [ head ] -> (if head = pr.Head then "bound-current" else "stale"), JStr head
        | _ -> "ambiguous", JNull

    let private phaseOf (issue: Read<IssueFact>) (pr: Read<PullRequestFact>) =
        match issue, pr with
        | Supplied i, _ when String.Equals(i.State, "closed", StringComparison.OrdinalIgnoreCase) -> "issue-closed"
        | Supplied _, NotRequested -> "no-pull-request-selected"
        | Supplied _, Unavailable _ -> "pull-request-unavailable"
        | Supplied _, Supplied p ->
            if p.Merged then "merged-awaiting-acceptance"
            elif p.State = "closed" then "pull-request-closed-unmerged"
            elif p.Draft then "pull-request-draft"
            else
                match p.Checks with
                | Supplied population ->
                    if population.Checks |> List.exists failing then "pull-request-open-checks-failing"
                    elif population.Checks |> List.exists pending || not population.Complete then "pull-request-open-checks-pending-or-incomplete"
                    else "pull-request-open-checks-green"
                | _ -> "pull-request-open-checks-unknown"
        | _ -> "unknown"

    /// Everything the view says, as ordered sections plus gaps. Pure.
    let private assemble (o: Observations) =
        let gaps = ResizeArray<Gap>()
        let add g = gaps.Add g
        let issueRef = o.Issue
        let subject = $"{issueRef.Repository}#{issueRef.Number}"

        // ---- issue and the six fields
        let sections, sectionGaps, issueBody =
            match o.IssueRead with
            | Supplied issue ->
                let values, sectionGaps = parseSections issue.Body
                values, sectionGaps, Some issue.Body
            | Unavailable reason ->
                add (gap "issue-unavailable" Obligation subject $"the owning issue could not be read: {reason}")
                Map.empty, [], None
            | NotRequested ->
                add (gap "issue-not-requested" Obligation subject "no owning issue observation was supplied")
                Map.empty, [], None
        sectionGaps |> List.iter add
        let textOrGap name =
            match Map.tryFind name sections with
            | Some text -> [ "text", JStr text; "bytes", JNum(int64 (bytesOf text)); "sha256", JStr(sha256 text) ]
            | None ->
                let code =
                    match sectionGaps |> List.tryFind (fun g -> g.Subject = name && g.Code <> "unrecognised-section") with
                    | Some g -> g.Code
                    | None -> if issueBody.IsNone then "issue-unavailable" else "missing-section"
                [ "text", JNull; "gap", JStr code ]

        // ---- custody (original uncertain effects, attempt)
        let custody =
            match o.Custody with
            | Supplied c ->
                let expected = $"{issueRef.Repository}#{issueRef.Number}"
                if String.Equals(c.Issue, expected, StringComparison.OrdinalIgnoreCase) then Some c
                else
                    add (gap "custody-identity-mismatch" Obligation subject $"the supplied operation custody names {c.Issue}, not {expected}; it is not used")
                    None
            | Unavailable reason ->
                add (gap "custody-unreadable" Obligation subject $"the supplied operation custody could not be read: {reason}")
                None
            | NotRequested -> None

        let uncertain =
            match o.Custody, custody with
            | _, Some c ->
                JObj [ "source", JStr "operation-custody"
                       "slice", JStr c.Slice
                       "terminal", optStr c.Terminal
                       "custodyGap", optStr c.Gap
                       "unknowns", JArr(c.Unknowns |> List.map JStr)
                       "note", JStr "Original effects stay Unknown until independently read back; this view neither retries, releases nor replays them." ]
            | Supplied _, None -> JObj [ "source", JStr "operation-custody-rejected"; "reason", JStr "identity-mismatch" ]
            | Unavailable reason, None -> JObj [ "source", JStr "operation-custody-unreadable"; "reason", JStr reason ]
            | NotRequested, None ->
                add (gap "original-effects-not-supplied" Context subject "no operation custody was supplied; the view makes no claim about original uncertain effects or reservations")
                JObj [ "source", JStr "not-supplied"; "assertion", JStr "no claim about original effects or reservations" ]

        // ---- owner and attempt
        if custody.IsNone then add (gap "attempt-unestablished" Context subject "the attempt is carried by operation custody, which was not usable")
        let owner =
            JObj(
                textOrGap "Owner"
                @ [ "attempt", optStr (custody |> Option.map _.Attempt)
                    "custodyOwner", optStr (custody |> Option.map _.Owner) ]
            )

        // ---- pull request, head, checks
        let prFact =
            match o.PullRequestRead with
            | Supplied pr ->
                match o.RequestedPullRequest with
                | Some n when n <> pr.Number ->
                    add (gap "pull-request-identity-mismatch" Obligation subject $"requested pull request {n} but observed {pr.Number}; it is not used")
                    None
                | _ -> Some pr
            | Unavailable reason ->
                add (gap "pull-request-unavailable" (if pullRequestRequired o.Action then Obligation else Context) subject $"the pull request could not be read: {reason}")
                None
            | NotRequested ->
                add (gap "pull-request-not-selected" (if pullRequestRequired o.Action then Obligation else Context) subject "no pull request was selected; absence of a pull request is not asserted")
                None
        let checkSummary, checkEvidence =
            match prFact with
            | None -> JNull, None
            | Some pr ->
                match pr.Checks with
                | Supplied population ->
                    if not population.Complete then
                        add (gap "checks-incomplete" Context subject "the native check population was larger than the page read; counts are lower bounds")
                    let total = population.Checks.Length
                    let ok = population.Checks |> List.filter successLike |> List.length
                    let bad = population.Checks |> List.filter failing
                    let wait = population.Checks |> List.filter pending
                    let summary =
                        JObj [ "total", JNum(int64 total); "success", JNum(int64 ok)
                               "failing", JNum(int64 bad.Length); "pending", JNum(int64 wait.Length)
                               "complete", JBool population.Complete
                               "readiness", JStr "not-evaluated" ]
                    let evidence =
                        (bad @ wait)
                        |> List.map (fun c -> JObj [ "name", JStr c.Name; "status", JStr c.Status; "conclusion", optStr c.Conclusion ])
                    summary, (if evidence.IsEmpty then None else Some(JArr evidence, not bad.IsEmpty))
                | Unavailable reason ->
                    add (gap "checks-unavailable" Context subject $"check evidence at the pull request head could not be read: {reason}")
                    JObj [ "unavailable", JStr reason ], None
                | NotRequested ->
                    add (gap "checks-not-requested" Context subject "check evidence was not requested")
                    JNull, None
        let originalHeadCurrent =
            match custody, prFact with
            | Some c, Some pr -> JBool(String.Equals(c.Head, pr.Head, StringComparison.OrdinalIgnoreCase))
            | _ -> JNull
        let source =
            match prFact with
            | Some pr ->
                let marker, markerHead = markerState pr
                JObj [ "repository", JStr issueRef.Repository
                       "pullRequest",
                       JObj [ "number", JNum(int64 pr.Number); "url", JStr pr.Url; "state", JStr pr.State
                              "draft", JBool pr.Draft; "merged", JBool pr.Merged
                              "head", JStr pr.Head; "headRef", JStr pr.HeadRef; "baseRef", JStr pr.BaseRef
                              "changedFiles", JNum(int64 pr.ChangedFiles) ]
                       "routineMarker", JObj [ "state", JStr marker; "head", markerHead ]
                       "originalOperationHead", optStr (custody |> Option.map _.Head)
                       "originalHeadCurrent", originalHeadCurrent
                       "checks", checkSummary ]
            | None -> JObj [ "repository", JStr issueRef.Repository; "pullRequest", JNull ]

        // ---- touch-set
        let nav = match o.Navigation with Supplied n -> Some n | _ -> None
        let fileState (path: string) =
            if isPattern path then [ "state", JStr "pattern" ]
            else
                match nav |> Option.bind (fun n -> Map.tryFind (path.TrimEnd('/')) n.Files) with
                | Some(File digest) -> [ "state", JStr "file"; "sha256", JStr digest ]
                | Some Directory -> [ "state", JStr "directory" ]
                | Some Absent -> [ "state", JStr "absent-in-workspace" ]
                | Some(Unreadable reason) -> [ "state", JStr "unreadable"; "reason", JStr reason ]
                | None -> [ "state", JStr "unchecked" ]
        let touchSet =
            match issueBody with
            | None -> JObj [ "declaration", JStr "unknown"; "gap", JStr "issue-unavailable" ]
            | Some body ->
                match TouchSet.parse body with
                | Undeclared ->
                    add (gap "touch-set-undeclared" Context subject "the issue declares no Paths: line; the touch-set is not asserted")
                    JObj [ "declaration", JStr "undeclared" ]
                | DeclaredNone -> JObj [ "declaration", JStr "declared-none" ]
                | DeclaredChore -> JObj [ "declaration", JStr "declared-any" ]
                | Types.TouchSet.Unreadable reason ->
                    add (gap "touch-set-unreadable" Context subject $"the touch-set could not be read: {reason}")
                    JObj [ "declaration", JStr "unreadable"; "reason", JStr reason ]
                | Declared tokens ->
                    let entries =
                        tokens
                        |> List.map (function
                            | Matchable path -> JObj([ "path", JStr path; "kind", JStr "matchable" ] @ fileState path)
                            | Unmatchable path ->
                                add (gap "touch-set-unmatchable" Context path "this Paths: token can never match a file and reserves nothing")
                                JObj [ "path", JStr path; "kind", JStr "unmatchable" ])
                    JObj [ "declaration", JStr "declared"; "paths", JArr entries ]

        // ---- done when, prerequisites, phase, deliverable, evidence handle
        let doneWhenReferences =
            match Map.tryFind "Done when" sections with
            | Some text ->
                distinctMatches urlPattern 0 text
                |> List.map (fun url -> JObj [ "url", JStr url; "state", JStr "not-fetched" ])
            | None -> []
        let doneWhen = JObj(textOrGap "Done when" @ [ "references", JArr doneWhenReferences ])
        let blockedRefs =
            match Map.tryFind "Blocked on" sections with
            | Some text -> distinctMatches refPattern 1 text |> List.map (fun r -> JObj [ "ref", JStr r; "state", JStr "not-fetched" ])
            | None -> []
        let prerequisites = JObj(textOrGap "Blocked on" @ [ "refs", JArr blockedRefs ])
        let phase =
            JObj [ "phase", JStr(phaseOf o.IssueRead o.PullRequestRead)
                   "nextCheckpoint", JObj(textOrGap "Next action") ]
        let deliverable = JObj(textOrGap "Current deliverable")
        let evidenceHandle =
            match Map.tryFind "Acceptance evidence" sections with
            | Some text ->
                JObj [ "present", JBool true; "bytes", JNum(int64 (bytesOf text)); "sha256", JStr(sha256 text)
                       "carried", JBool false
                       "note", JStr "Acceptance evidence is a history; it is a retrieval handle here, never inlined." ]
            | None -> JObj(("present", JBool false) :: (textOrGap "Acceptance evidence" |> List.filter (fun (k, _) -> k = "gap")))

        // ---- navigation
        let instructions =
            instructionPaths o.Action
            |> List.map (fun path ->
                match nav |> Option.bind (fun n -> Map.tryFind path n.Files) with
                | Some(File digest) -> JObj [ "path", JStr path; "state", JStr "file"; "sha256", JStr digest ]
                | Some Absent ->
                    add (gap "instruction-missing" Context path "an applicable governing instruction file is absent in the workspace")
                    JObj [ "path", JStr path; "state", JStr "absent-in-workspace" ]
                | Some Directory | Some(Unreadable _) ->
                    add (gap "instruction-unreadable" Context path "an applicable governing instruction path is not a readable file")
                    JObj [ "path", JStr path; "state", JStr "unreadable" ]
                | None -> JObj [ "path", JStr path; "state", JStr "unchecked" ])
        let atlas =
            match o.Navigation with
            | NotRequested ->
                add (gap "workspace-not-supplied" Context subject "no workspace was supplied; source paths and Atlas IDs are unverified")
                JObj [ "archive", JStr "not-supplied" ]
            | Unavailable reason ->
                add (gap "workspace-unavailable" Context subject $"the workspace could not be read: {reason}")
                JObj [ "archive", JStr "unavailable"; "reason", JStr reason ]
            | Supplied n ->
                match n.Atlas with
                | Supplied entries ->
                    let namespaces = entries |> List.map (fun e -> e.Id.Split('/')[0]) |> Set.ofList
                    let text = String.concat "\n" [ defaultArg issueBody ""; (match prFact with Some pr -> pr.Body | None -> "") ]
                    let candidates =
                        atlasPattern.Matches text
                        |> Seq.map (fun m -> m.Groups[1].Value.TrimEnd('.', '-', '/'))
                        |> Seq.filter (fun id -> namespaces.Contains(id.Split('/')[0]))
                        |> Seq.distinct
                        |> Seq.truncate 32
                        |> List.ofSeq
                    let byId = entries |> List.map (fun e -> e.Id, e) |> Map.ofList
                    let resolved, unresolved = candidates |> List.partition byId.ContainsKey
                    let resolvedJson =
                        resolved |> List.map (fun id ->
                            let e = byId[id]
                            JObj [ "id", JStr e.Id; "kind", JStr e.Kind; "status", JStr e.Status; "title", JStr e.Title; "record", JStr e.Record ])
                    unresolved |> List.iter (fun id -> add (gap "atlas-id-unresolved" Context id "an Atlas-ID-shaped reference in the issue or pull request names no record in the workspace archive"))
                    JObj [ "archive", JStr "supplied"; "resolved", JArr resolvedJson; "unresolved", JArr(unresolved |> List.map JStr)
                           "note", JStr "Records are curated knowledge, not authority; verify the cited source before reliance." ]
                | Unavailable reason ->
                    add (gap "atlas-archive-unavailable" Context subject $"the Atlas archive could not be read: {reason}")
                    JObj [ "archive", JStr "unavailable"; "reason", JStr reason ]
                | NotRequested -> JObj [ "archive", JStr "not-requested" ]
        let navigation =
            JObj [ "governingInstructions", JArr instructions
                   "instructionPrecedence", JStr "Governing instructions remain complete and higher priority than this view."
                   "atlas", atlas ]

        let prMandatory = pullRequestRequired o.Action
        let ordered =
            [ { Key = "doneWhen"; Priority = 1; Mandatory = true; Value = doneWhen; Retrieval = issueRetrieval issueRef "Done when" }
              { Key = "uncertainEffects"; Priority = 2; Mandatory = true; Value = uncertain
                Retrieval = "rerun context-view with --custody PATH (the original operation cursor file; read-only)" }
              { Key = "owner"; Priority = 3; Mandatory = true; Value = owner; Retrieval = issueRetrieval issueRef "Owner" }
              { Key = "touchSet"; Priority = 4; Mandatory = true; Value = touchSet; Retrieval = issueRetrieval issueRef "(Paths: declaration)" }
              { Key = "source"; Priority = 5; Mandatory = prMandatory; Value = source
                Retrieval = $"gh pr view PR --repo {issueRef.Repository} --json state,isDraft,headRefOid,baseRefName,changedFiles,body" }
              { Key = "prerequisites"; Priority = 6; Mandatory = true; Value = prerequisites; Retrieval = issueRetrieval issueRef "Blocked on" }
              { Key = "phase"; Priority = 7; Mandatory = true; Value = phase; Retrieval = issueRetrieval issueRef "Next action" }
              { Key = "currentDeliverable"; Priority = 8; Mandatory = true; Value = deliverable; Retrieval = issueRetrieval issueRef "Current deliverable" } ]
            @ (match checkEvidence with
               | Some(value, anyFailing) ->
                   [ { Key = "checkEvidence"; Priority = 9; Mandatory = anyFailing; Value = value
                       Retrieval = $"gh pr checks PR --repo {issueRef.Repository}" } ]
               | None -> [])
            @ [ { Key = "acceptanceEvidence"; Priority = 10; Mandatory = false; Value = evidenceHandle; Retrieval = issueRetrieval issueRef "Acceptance evidence" }
                { Key = "navigation"; Priority = 11; Mandatory = false; Value = navigation
                  Retrieval = "rerun context-view with --workspace DIR and a larger --cap-bytes" } ]
        ordered, (List.ofSeq gaps |> List.distinct)

    // ---------------------------------------------------------------- assembly and cap

    let private gapJson (g: Gap) =
        JObj [ "code", JStr g.Code
               "severity", JStr(match g.Severity with Obligation -> "obligation" | Context -> "context")
               "subject", JStr g.Subject
               "detail", JStr g.Detail ]

    let private issueHeader (o: Observations) =
        let basics = [ "repository", JStr o.Issue.Repository; "number", JNum(int64 o.Issue.Number) ]
        match o.IssueRead with
        | Supplied i ->
            JObj(basics @ [ "title", JStr i.Title; "state", JStr i.State; "updatedAt", JStr i.UpdatedAt
                            "bodyBytes", JNum(int64 (bytesOf i.Body)); "bodySha256", JStr(sha256 i.Body) ])
        | Unavailable reason -> JObj(basics @ [ "unavailable", JStr reason ])
        | NotRequested -> JObj(basics @ [ "unavailable", JStr "not-requested" ])

    let private authority =
        "none; read-only projection of native facts at the stated revisions. Governing instructions remain complete and higher priority. Not a packet, a required return, a status table or a ledger."

    let build (o: Observations) =
        if o.CapBytes < MinimumCapBytes || o.CapBytes > MaximumCapBytes then Error(CapOutOfBounds(o.CapBytes, MinimumCapBytes))
        else
            let sections, gaps = assemble o
            let hasObligationGap = gaps |> List.exists (fun g -> g.Severity = Obligation)
            let compose (included: Section list) (omitted: Section list) (fullBytes: int option) =
                let omissions =
                    omitted
                    |> List.map (fun s ->
                        let rendered = render s.Value
                        { Section = s.Key; Mandatory = s.Mandatory; Bytes = bytesOf rendered; Sha256 = sha256 rendered; Retrieval = s.Retrieval })
                let status = if omitted.IsEmpty && not hasObligationGap then Complete else Incomplete
                let head =
                    [ "schema", JStr Schema
                      "status", JStr(match status with Complete -> "complete" | Incomplete -> "incomplete")
                      "authority", JStr authority
                      "action", JStr(actionName o.Action)
                      "capBytes", JNum(int64 o.CapBytes)
                      "issue", issueHeader o
                      "gaps", JArr(gaps |> List.map gapJson)
                      "omissions",
                      JArr(
                          omissions
                          |> List.map (fun m ->
                              JObj [ "section", JStr m.Section; "mandatory", JBool m.Mandatory; "bytes", JNum(int64 m.Bytes)
                                     "sha256", JStr m.Sha256; "retrieval", JStr m.Retrieval ])
                      ) ]
                    @ (match fullBytes with
                       | Some n -> [ "fullViewBytes", JNum(int64 n) ]
                       | None -> [])
                let body = included |> List.map (fun s -> s.Key, s.Value)
                let json = render (JObj(head @ body))
                status, json, omissions
            let _, fullJson, _ = compose sections [] None
            let fullBytes = bytesOf fullJson
            let finish (status, json: string, omissions) =
                Ok { Status = status; Json = json; Bytes = bytesOf json; Sha256 = sha256 json; Gaps = gaps; Omissions = omissions }
            if fullBytes <= o.CapBytes then finish (compose sections [] None)
            else
                // Drop whole sections from the lowest priority up; each is listed, never silently cut.
                let rec shrink (included: Section list) (omitted: Section list) =
                    match List.rev included with
                    | [] -> Error(CapOutOfBounds(o.CapBytes, bytesOf (let _, j, _ = compose [] omitted (Some fullBytes) in j)))
                    | last :: restReversed ->
                        let included' = List.rev restReversed
                        let omitted' = last :: omitted
                        let ((_, json, _) as composed) = compose included' omitted' (Some fullBytes)
                        if bytesOf json <= o.CapBytes then finish composed else shrink included' omitted'
                shrink sections []
