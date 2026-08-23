namespace FS.GG.Coord.Cli

/// JSON snapshot boundary for the pure claim-to-done lifecycle decision.
module DeliveryApplication =
    open System
    open System.IO
    open System.Text.Json
    open System.Text.RegularExpressions
    open FS.GG.Coord
    open FS.GG.Coord.Cli.Options

    let private eprint (message: string) = Console.Error.WriteLine(message)

    let private input opts =
        match opts.SnapshotFile with
        | Some path -> File.ReadAllText path
        | None -> Console.In.ReadToEnd()

    let private required (name: string) (element: JsonElement) : JsonElement =
        match element.TryGetProperty name with
        | true, value -> value
        | _ -> invalidArg name "required field is missing"

    let private readString (name: string) (element: JsonElement) : string =
        let value = required name element
        if value.ValueKind <> JsonValueKind.String then invalidArg name "must be a string"
        let parsed = value.GetString()
        if String.IsNullOrWhiteSpace parsed then invalidArg name "must not be empty"
        parsed

    let private readBoolean (name: string) (element: JsonElement) : bool =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> invalidArg name "must be a boolean"

    let private readInteger (name: string) (element: JsonElement) : int =
        let value = required name element
        match value.TryGetInt32() with
        | true, parsed -> parsed
        | _ -> invalidArg name "must be a 32-bit integer"

    let private readOptionalInteger (name: string) (element: JsonElement) : int option =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | _ -> Some(readInteger name element)

    /// `declaredPaths` accepts its original wire shape — a plain array of strings, read as tokens
    /// actually declared — or one of three tagged objects distinguishing the ways a touch-set can be
    /// empty-ish (.github#2233 acceptance 4): `{"declaredNone": true}` (an explicit, read `Paths:
    /// none`), `{"undeclared": true}` (a read body with no `Paths:` line at all), or `{"unread":
    /// "<reason>"}` (the body was never read). The array form stays byte-for-byte what every existing
    /// producer (including `tests/coord-engine-e2e/writes.sh`, outside this item's declared `Paths:`)
    /// already emits, so this is additive rather than a breaking wire change.
    let private declaredPaths (element: JsonElement) : Delivery.DeclaredPaths =
        let value = required "declaredPaths" element
        match value.ValueKind with
        | JsonValueKind.Array ->
            let paths =
                value.EnumerateArray()
                |> Seq.map (fun item ->
                    if item.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(item.GetString()) then
                        invalidArg "declaredPaths" "must contain non-empty strings"
                    item.GetString())
                |> List.ofSeq
            Delivery.Known paths
        | JsonValueKind.Object ->
            if fst (value.TryGetProperty "unread") then
                Delivery.Unread(readString "unread" value)
            elif fst (value.TryGetProperty "declaredNone") then
                Delivery.DeclaredNone
            elif fst (value.TryGetProperty "undeclared") then
                Delivery.Undeclared
            else
                invalidArg "declaredPaths" "object must be {\"unread\": reason}, {\"declaredNone\": true}, or {\"undeclared\": true}"
        | _ -> invalidArg "declaredPaths" "must be an array of paths or a tagged object naming why it has none"

    let private readOptionalString (name: string) (element: JsonElement) : string option =
        let value = required name element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.String when not (String.IsNullOrWhiteSpace(value.GetString())) -> Some(value.GetString())
        | _ -> invalidArg name "must be null or a non-empty string"

    /// A field an older snapshot producer may not emit at all.  Absent means "this producer makes no
    /// diff-audit assertion" — which is exactly the behavior before .github#2144 — and is deliberately
    /// NOT read as a producer asserting `false` about an audit it measured and cleared.
    let private readBooleanOrDefault (name: string) (fallback: bool) (element: JsonElement) : bool =
        match element.TryGetProperty name with
        | true, _ -> readBoolean name element
        | _ -> fallback

    let private readOptionalStringOrAbsent (name: string) (element: JsonElement) : string option =
        match element.TryGetProperty name with
        | true, _ -> readOptionalString name element
        | _ -> None

    let private review (element: JsonElement) : Driver.ReviewChain option =
        let value = required "review" element
        match value.ValueKind with
        | JsonValueKind.Null -> None
        | JsonValueKind.Object ->
            let rounds =
                let raw = required "rounds" value
                if raw.ValueKind <> JsonValueKind.Array then invalidArg "review.rounds" "must be an array"
                raw.EnumerateArray()
                |> Seq.map (fun item ->
                    match item.TryGetInt32() with
                    | true, number -> number
                    | _ -> invalidArg "review.rounds" "must contain integers")
                |> List.ofSeq
            let markerValid = readBoolean "markerValid" value
            let subject = readOptionalStringOrAbsent "subject" value
            let claimGeneration = readOptionalStringOrAbsent "claimGeneration" value
            let baseSha = readOptionalStringOrAbsent "baseSha" value
            let criticIdentity = readOptionalString "criticIdentity" value
            let headSha = readOptionalString "headSha" value
            let repairPhase = readBoolean "repairPhase" value
            let checksGreen = readBoolean "checksGreen" value
            let hostAccepted = readBoolean "hostAccepted" value
            let runtimeRouteEvidence = readOptionalString "routeNotMeaningfulReason" value |> Option.map Driver.NotMeaningful
            let diffAuditRequired = readBooleanOrDefault "diffAuditRequired" false value
            let diffAuditHead = readOptionalStringOrAbsent "diffAuditHead" value
            Some
                ({ MarkerValid = markerValid;
                  Subject = subject;
                  ClaimGeneration = claimGeneration;
                  BaseSha = baseSha;
                  CriticIdentity = criticIdentity;
                  HeadSha = headSha;
                  Rounds = rounds;
                  RepairPhase = repairPhase;
                  ChecksGreen = checksGreen;
                  HostAccepted = hostAccepted;
                  RuntimeRouteEvidence = runtimeRouteEvidence;
                  DiffAuditRequired = diffAuditRequired;
                  DiffAuditHead = diffAuditHead } : Driver.ReviewChain)
        | _ -> invalidArg "review" "must be an object or null"

    let private obligations (element: JsonElement) : Delivery.Obligation list =
        let value = required "obligations" element
        if value.ValueKind <> JsonValueKind.Array then invalidArg "obligations" "must be an array"
        value.EnumerateArray()
        |> Seq.map (fun obligation ->
            if obligation.ValueKind <> JsonValueKind.Object then invalidArg "obligations" "must contain objects"
            ({ Id = readString "id" obligation
               Kind = readString "kind" obligation
               Evidence = readOptionalString "evidence" obligation
               HeadSha = readString "headSha" obligation
               Verified = readBoolean "verified" obligation }: Delivery.Obligation))
        |> List.ofSeq

    let private obligationId = "[a-z0-9][a-z0-9_.-]*"
    let private obligationKind = "[a-z0-9][a-z0-9_-]*"
    let private deliveryHead = "[0-9A-Za-z._-]+"
    let private fieldValue = "[^ ]+"

    let private obligationDeclaration =
        Regex($"^<!-- fsgg:delivery-obligation id=(?<id>{obligationId}) kind=(?<kind>{obligationKind}) head=(?<head>{deliveryHead}) -->$", RegexOptions.Compiled)

    let private obligationReceipt =
        Regex($"^<!-- fsgg:delivery-receipt id=(?<id>{obligationId}) head=(?<head>{deliveryHead}) evidence=(?<evidence>{fieldValue}) -->$", RegexOptions.Compiled)

    let private declarationFields =
        Regex($"^<!-- fsgg:delivery-obligation id=(?<id>{fieldValue}) kind=(?<kind>{fieldValue}) head=(?<head>{fieldValue}) -->$", RegexOptions.Compiled)

    let private receiptFields =
        Regex($"^<!-- fsgg:delivery-receipt id=(?<id>{fieldValue}) head=(?<head>{fieldValue}) evidence=(?<evidence>{fieldValue}) -->$", RegexOptions.Compiled)

    // THE LEADING-LINE RULE (.github#2347), applying `.github#2221`'s established correction
    // ("a marker is evidence only as a WHOLE LINE inside the comment's LEADING MARKER BLOCK", never
    // the comment's entire body) to the three delivery markers, which never received it. A delivery
    // marker carries all of its fields inline on one line — unlike a review marker, which pairs a
    // bare marker line with separate `key: value` field lines below it — so its "leading marker
    // block" is exactly the comment's first line: the run of lines, from byte 0, that could be this
    // one marker is never longer than one line for a marker whose own grammar is single-line. Prose
    // that follows on later lines (the org's universal delivery-obligation/receipt writing style —
    // marker line, blank line, explanation) is therefore never part of the marker and is never
    // matched against it. A marker merely quoted later in the body — not the comment's own leading
    // line — still fails this match and stays inert, preserving the `.github#2264` round-1 anchoring
    // fix for the sibling read path.
    //
    // ONE FUNCTION, AND THE INDENT LIMIT LIVES INSIDE IT (.github#2544, round-1 repair). `leadingLine` is
    // the ONLY place that decides what "leading" means — the candidate pre-filter, the three parses and
    // the `none` equality all ask it rather than re-deriving it, which is what criterion 1 requires.
    // Criterion 1 constrains WHERE the rule lives, not how permissive it is, so narrowing it here keeps
    // that criterion satisfied rather than trading against it.
    //
    // AND THE LIMIT IS COMMONMARK'S, NOT AN ARBITRARY BUDGET. Leading blank lines and up to THREE spaces
    // of indentation render invisibly, so a marker behind them is the comment's leading line in every
    // sense a reader has access to. FOUR spaces — or a tab, which is one tab stop — opens a CommonMark
    // INDENTED CODE BLOCK: the marker is then visibly a code sample, and treating it as a declaration is
    // exactly "making a quoted marker live again", which `.github#2544`'s governing sentence forbids and
    // which the generated `fsgg-protocol:review-policy` block already names in as many words ("inside a
    // fence, AN INDENTED CODE BLOCK, or prose that only mentions it" is inert — `independent-review.md:16`).
    //
    // The round-1 critic measured why this is not cosmetic: with an unlimited trim, ONE added comment
    // carrying an indented code sample destroys a VALID declaration already on the PR (the `none` sentinel
    // then collides with a phantom obligation), and an indented declaration+receipt pair reads
    // `Verified = true` — fail-OPEN, the one direction this subsystem must never move. A bystander posting
    // documentation could do that to somebody else's PR.
    //
    // AND THE LIMIT IS NOT WRITTEN ONLY HERE ANY MORE (.github#2563). `check-kit-published-coherence.py`
    // deliberately restates this filter — an arm STRICTER than the engine would call a LIVE declaration
    // absent, which is the same invisibility one layer down — but until #2563 the two copies were
    // coupled by nothing except a docstring saying so. A one-sided edit reddened that side's fixtures; a
    // COORDINATED one-sided edit, moving one constant and that same language's own legs together,
    // passed both, and that is what a careful engineer does when they believe they are fixing a bug.
    // `tests/delivery-leading-line/corpus.json` is now the single statement of this boundary, graded
    // through BOTH real entry points — `obligationsFromComments` here, `obligation_declarations` there.
    // Changing the test below reds that corpus; editing the corpus to restore it reds the Python gate.
    let private leadingLine (text: string) =
        // The first line carrying any content, exactly as written — indentation included, because the
        // indentation is the thing being measured.
        let firstContentLine =
            text.Replace("\r\n", "\n").Split('\n')
            |> Array.tryFind (fun line -> line.Trim() <> "")
        let indentOf (line: string) =
            let mutable i = 0
            while i < line.Length && (line.[i] = ' ' || line.[i] = '\t') do
                i <- i + 1
            line.Substring(0, i)
        match firstContentLine with
        | Some line when (let indent = indentOf line in indent.Contains '\t' || indent.Length >= 4) ->
            // An indented code block is not a leading marker line at all. Returning the line AS WRITTEN
            // means no prefix and no marker grammar can match it, so the comment is inert.
            line
        | _ ->
            // Every other body reaches the original trim, byte for byte — so leading blank lines and up
            // to three spaces behave exactly as they did, and nothing else in this module moves.
            let trimmed = text.Trim().Replace("\r\n", "\n")
            match trimmed.IndexOf '\n' with
            | -1 -> trimmed
            | index -> trimmed.Substring(0, index)

    let private declarationPrefix = "<!-- fsgg:delivery-obligation"
    // `<!-- fsgg:delivery-obligations none head=<sha> -->` extends `declarationPrefix` (plural, then a
    // space), so these two prefixes between them name all three delivery markers.
    let private receiptPrefix = "<!-- fsgg:delivery-receipt"

    // THE CANDIDATE PRE-FILTER ASKS THE PARSER'S OWN QUESTION, AND ONLY HERE (.github#2544). Before this,
    // `obligationsFromComments` selected candidates with a RAW, untrimmed `Body.StartsWith` while every
    // parse below tested `leadingLine`, which trims first. The pre-filter was therefore strictly STRICTER
    // than the parser it fed: a body opening with a newline or a space — which `leadingLine` was written
    // to accept, and does accept — was discarded before `leadingLine` ever ran, and the item read back as
    // though nothing had been declared at all. Nobody decided that; two places simply answered different
    // questions and one of them ran first. A leading newline is what heredocs, `gh api --field` payloads
    // and comment editors add for free, so the trigger was authoring mechanics that the rendered comment
    // does not show. Routing selection through `leadingLine` makes the agreement structural rather than
    // coincidental. It does NOT loosen the inertness boundary `.github#2347` acceptance 2 and the
    // `.github#2264` round-1 anchoring fix protect: a marker that is not the comment's own leading line —
    // quoted in prose, or on a line inside a fenced block whose leading line is the fence — still fails
    // this test and stays inert.
    let private leadsWith (prefix: string) (comment: Driver.ReviewComment) =
        (leadingLine comment.Body).StartsWith prefix

    // AN INVISIBLE DECLARATION IS NOT A MALFORMED ONE, AND THAT IS THE WHOLE PROBLEM (.github#2544).
    // A marker present but not leading is inert BY DESIGN, and that design stays. What changes is only the
    // DIAGNOSTIC: `"delivery obligations are undeclared"` names no comment, so an author who posted a real
    // declaration below a heading and an inspecting host both read "you declared nothing" when the truth is
    // "comment N carries the marker below its leading line". A malformed declaration announces itself; an
    // invisible one is indistinguishable from never having been written, which is why four independent
    // lanes in one session each posted a heading above their marker believing they had declared. This
    // predicate decides nothing about the parse — it only supplies the comment id for that message.
    //
    // AND IT MUST REACH THE INDENTED LEADING MARKER TOO (round-1 repair). Narrowing `leadingLine` above
    // sends a four-space-indented declaration back to being ignored — so if this predicate still only
    // looked BELOW the leading line, that declaration would be silently invisible again, which is the
    // exact failure this row exists to kill and the part a narrowing repair most easily drops. So the
    // scan is over EVERY line of the body, including the first: the leading-line guard below is what
    // keeps a genuine candidate from being described as a quotation, and it no longer needs the
    // leading/below split to do that.
    let private carriesInertMarker (comment: Driver.ReviewComment) =
        let isMarkerLine (line: string) =
            let line = line.Trim()
            line.StartsWith declarationPrefix || line.StartsWith receiptPrefix
        // A comment that already LEADS with a marker is a candidate, not an inert quotation, even when it
        // also quotes one further down; naming it here would misdescribe it.
        not (leadsWith declarationPrefix comment || leadsWith receiptPrefix comment)
        && (comment.Body.Replace("\r\n", "\n").Split('\n') |> Array.exists isMarkerLine)

    let private malformedField (comment: Driver.ReviewComment) (kind: string) (fields: Regex) =
        let matched = fields.Match(leadingLine comment.Body)
        if not matched.Success then Error $"delivery {kind} comment {comment.Id} has malformed body"
        elif not (Regex($"^{obligationId}$").IsMatch(matched.Groups.["id"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed id"
        elif kind = "obligation declaration" && not (Regex($"^{obligationKind}$").IsMatch(matched.Groups.["kind"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed kind"
        elif not (Regex($"^{deliveryHead}$").IsMatch(matched.Groups.["head"].Value)) then Error $"delivery {kind} comment {comment.Id} has malformed head"
        else Error $"delivery {kind} comment {comment.Id} is malformed"

    let obligationsFromComments (headSha: string) (comments: Driver.ReviewComment list) : Result<Delivery.Obligation list, string> =
        let declarations = comments |> List.filter (leadsWith declarationPrefix)
        let receipts = comments |> List.filter (leadsWith receiptPrefix)
        let none = $"<!-- fsgg:delivery-obligations none head=%s{headSha} -->"
        if declarations |> List.exists (fun comment -> leadingLine comment.Body = none) then
            if declarations |> List.exists (fun comment -> leadingLine comment.Body <> none) || not (List.isEmpty receipts) then
                Error "the no-obligations declaration cannot be combined with obligation declarations or receipts"
            else Ok []
        elif List.isEmpty declarations then
            // Still `undeclared` — the parse is unchanged and the marker stays inert — but say WHERE the
            // ignored marker is when there is one to point at (.github#2544).
            match comments |> List.tryFind carriesInertMarker with
            | Some comment ->
                // THE ADVICE IS CONDITIONAL, DELIBERATELY (round-1 repair). The previous wording told the
                // reader to "edit that comment to lead with it" — wrong for a documentation comment, and
                // under the indented-code-block case it was advice to perform the exact mutation that
                // turns a code sample into a live declaration. Say what is true and let the author decide.
                Error
                    $"delivery obligations are undeclared: comment {comment.Id} carries a delivery marker that is not this comment's leading line — a marker below the first line, or indented four or more spaces (or by a tab) into a code block, is a quotation and stays inert. If it was meant to declare, post a comment whose very first line is that marker, indented no more than three spaces; if it is a code sample or documentation, it is correctly inert and nothing has been declared yet"
            | None -> Error "delivery obligations are undeclared"
        else
            let parsedDeclarations =
                declarations
                |> List.map (fun comment ->
                    let matched = obligationDeclaration.Match(leadingLine comment.Body)
                    if not matched.Success then malformedField comment "obligation declaration" declarationFields
                    elif matched.Groups.["head"].Value <> headSha then
                        Error $"delivery obligation declaration comment {comment.Id} is stale for head {headSha}; edit it in place or delete it, because adding a declaration cannot repair it"
                    else Ok(matched.Groups.["id"].Value, matched.Groups.["kind"].Value))
            let firstError values = values |> List.tryPick (function Error error -> Some error | Ok _ -> None)
            match parsedDeclarations |> firstError with
            | Some error -> Error error
            | None ->
                let declarations = parsedDeclarations |> List.choose Result.toOption
                let ids = declarations |> List.map fst
                if ids |> List.distinct |> List.length <> List.length ids then Error "delivery obligation ids must be unique"
                else
                    let parsedReceipts =
                        receipts
                        |> List.map (fun comment ->
                            let matched = obligationReceipt.Match(leadingLine comment.Body)
                            if not matched.Success then malformedField comment "obligation receipt" receiptFields
                            elif matched.Groups.["head"].Value <> headSha then Error "a delivery obligation receipt is stale"
                            else Ok(matched.Groups.["id"].Value, matched.Groups.["evidence"].Value))
                    match parsedReceipts |> firstError with
                    | Some error -> Error error
                    | None ->
                        let receipts = parsedReceipts |> List.choose Result.toOption |> Map.ofList
                        if receipts |> Map.exists (fun id _ -> not (List.contains id ids)) then Error "a delivery obligation receipt names no declared obligation"
                        else
                            declarations
                            |> List.map (fun (id, kind) ->
                                let evidence = Map.tryFind id receipts
                                ({ Id = id; Kind = kind; Evidence = evidence; HeadSha = headSha; Verified = evidence.IsSome }: Delivery.Obligation))
                            |> Ok

    // ============================================================================================
    // THE MERGE ELECTION (.github#2395, §11.2 slice 3 of .github#1858)
    // ============================================================================================
    //
    // §11.2 row 3 declares TWO acts — *"`delivery` posts the merge election, THEN writes the PR
    // authorization marker NAMING it and bound to head"* — and only the second landed. This is the
    // pure half of the first: the marker's text, and the parse that recognises one. The IO half
    // (read the item, post when absent, hand the grant to the authorization) is `Client`'s.
    //
    // THE SPELLING IS NOT INVENTED HERE. `scripts/check-claim-fence.py` already fixed it as a
    // READER, because writing the election was this row's job and the reader could not wait for it:
    // `REQUIRED_ELECTION_FIELDS = ("v", "opkey", "item", "gen", "receiver", "op")`, matched by
    // `ELECTION_MARKER_RE`. This producer emits exactly those six, plus `pr=` (below), and every
    // regex in this section mirrors that gate's rather than inventing a second grammar.
    //
    // THE ANCHORING IS THE GATE'S, AND IT IS STRICTER THAN THE DELIVERY MARKERS ABOVE. The fence
    // matches the election with `re.match` on the RAW comment body — `^<!--\s*fsgg:merge-election\s`,
    // no `MULTILINE`, and NO `.strip()` anywhere on the path (`read_item_state` reads `comment["body"]`
    // and matches it directly). So a leading newline or a leading space makes an election INVISIBLE
    // to the only reader that grades it. This module therefore does NOT reuse `leadingLine`, which
    // trims: it anchors at byte 0 exactly as the gate does, so a marker this parse accepts is one the
    // gate accepts and — the direction that matters for a producer — a body this module composes
    // begins with the marker at byte 0. The gate's own reason for the stricter anchor is that check 4
    // is what everything else is grounded in: a comment that merely QUOTES an election — a review
    // note, a design excerpt, this repository's design doc pasted into a comment — must not enter an
    // election it never joined.
    //
    // A NEW PREFIX IS SAFE, and that is a property rather than a hope: the claim CAS matches
    // `fsgg:claim` and nothing else (`src/FS.GG.Coord.GitHub/Reads.fs`, `markerRe`), so an election
    // marker is invisible to the lock by construction and can forge no tenancy.

    // One election as it sits on the item: the COMMENT ID — which is the whole of `grant=`, because
    // it is server-assigned and no caller chooses it — and the marker's raw fields.
    //
    // `Fields` is deliberately a raw `Map` rather than a record: the gate reads unknown fields
    // tolerantly and this producer must be able to see an election written by an older or newer
    // engine without failing to parse it. Nothing here validates; `Client` decides which elections
    // are this delivery target's, and the fence decides which one wins.
    type Election = { Id: int64; Fields: Map<string, string> }

    // `RegexOptions.Singleline` IS Python's `re.DOTALL`, and it is load-bearing for the same reason
    // the gate gives: the design doc's own spelling of this marker spans lines. `^` with no
    // `Multiline` anchors at position 0 of the input in .NET exactly as Python's `^` does without
    // `re.MULTILINE`, so this is the gate's `re.match(r"^...")` and not an approximation of it.
    let private electionMarkerPattern =
        Regex(@"^<!--\s*fsgg:merge-election\s(?<fields>.*?)-->", RegexOptions.Compiled ||| RegexOptions.Singleline)

    // The gate's `FIELD_RE`, character for character. Values are non-whitespace by construction —
    // none of these fields ever legitimately contains a space.
    let private electionFieldPattern =
        Regex(@"(?<k>[A-Za-z]+)=(?<v>\S+)", RegexOptions.Compiled)

    // The exact election text `delivery` appends to the item.
    //
    // Six of the seven fields are `REQUIRED_ELECTION_FIELDS`. The seventh, `pr=`, is this producer's
    // IDEMPOTENCE DISCRIMINATOR and it is why repeating a `delivery` call is safe. The fence ignores
    // fields it does not require, so it costs the reader nothing; it earns its place on the write
    // side, twice over:
    //
    //   * WITHOUT it, a second `delivery` call for the same item would post a SECOND election under
    //     the same opkey. Comment ids are monotone, so that election is strictly HIGHER, and an
    //     authorization naming it loses the gate's own lowest-id comparison — the pull request would
    //     be refused for the rest of that claim generation. Posting unconditionally is not merely
    //     wasteful; it is self-denial.
    //   * With a LAXER rule — "reuse any election bearing this opkey" — a second executor delivering
    //     the same item under one generation through a DIFFERENT pull request would inherit the
    //     first executor's grant and both would pass check 4. That would neuter the one guarantee
    //     the election exists to provide: design §4.2, *"at most one merge takes effect per (item,
    //     generation, receiver)"*. Keyed on the pull request, each contender posts its own election
    //     and only the lowest id wins, which is the refusal the fence's own check-4 message
    //     describes.
    //
    // `op=merge` is `Operation.wire Operation.Merge`; it is spelled literally here because this
    // marker is a wire form and `Operation.wire` is the authority for the spelling rather than a
    // value to interpolate into a template whose other six fields are literals too — the opkey it
    // keys is composed through `Operation.compose`, which is where the vocabulary is actually load-
    // bearing.
    let electionMarker (opkey: string) (item: string) (gen: string) (receiver: string) (pr: int) : string =
        $"<!-- fsgg:merge-election v=1 opkey=%s{opkey} item=%s{item} gen=%s{gen} receiver=%s{receiver} op=merge pr=%d{pr} -->"

    // Every election on the item, one per comment whose body OPENS with the marker.
    //
    // A comment carrying no election, or quoting one below its first byte, yields nothing — it is
    // not an election, exactly as the gate reads it.
    let electionsFromComments (comments: Driver.ReviewComment list) : Election list =
        comments
        |> List.choose (fun comment ->
            let matched = electionMarkerPattern.Match comment.Body
            if not matched.Success then
                None
            else
                let fields =
                    electionFieldPattern.Matches(matched.Groups.["fields"].Value)
                    |> Seq.map (fun m -> m.Groups.["k"].Value, m.Groups.["v"].Value)
                    |> Map.ofSeq
                Some { Id = comment.Id; Fields = fields })

    // The elections THIS delivery target already owns — same operation key, same pull request.
    //
    // Deliberately NOT "every election bearing this opkey": see `electionMarker`'s second bullet.
    // The wider set is the candidate set the FENCE computes, and the whole point of the fence is
    // that this producer does not get to compute it.
    let electionsOwnedBy (opkey: string) (pr: int) (elections: Election list) : Election list =
        elections
        |> List.filter (fun election ->
            election.Fields.TryFind "opkey" = Some opkey
            && election.Fields.TryFind "pr" = Some(string pr))

    // The fence elects the lowest comment id for the complete operation tuple. Delivery must make
    // the same observation before it appends another contender: once a different pull request won
    // this generation, a replacement can only produce a higher, permanently losing election.
    let winningElection
        (opkey: string)
        (item: string)
        (gen: string)
        (receiver: string)
        (elections: Election list)
        : Election option =
        elections
        |> List.filter (fun election ->
            election.Fields.TryFind "v" = Some "1"
            && election.Fields.TryFind "opkey" = Some opkey
            && election.Fields.TryFind "item" = Some item
            && election.Fields.TryFind "gen" = Some gen
            && election.Fields.TryFind "receiver" = Some receiver
            && election.Fields.TryFind "op" = Some "merge")
        |> List.sortBy _.Id
        |> List.tryHead

    /// The live adapter must consume its delivery receipt and prove that the same claim generation
    /// still wins immediately before it asks GitHub to merge.  Keeping this boundary pure makes the
    /// no-write branch explicit and independently testable.
    type LandingAuthorization =
        | MergeAuthorized
        | MergeRefused of reason: string

    type LandingReceipt<'result> =
        { HeadSha: string
          BaseSha: string
          Result: 'result }

    let authorizeGuardedLanding freshnessToken actionKey facts currentClaimGeneration =
        match Delivery.advance freshnessToken actionKey facts with
        | Delivery.NoVerdict reason -> MergeRefused reason
        | Delivery.Next transition when transition.Action <> Delivery.GuardedLand ->
            MergeRefused "delivery receipt does not authorize guarded landing"
        | Delivery.Next _ when Some facts.Freshness.ClaimGeneration <> currentClaimGeneration ->
            MergeRefused "delivery claim generation changed after inspection; GitHub merge was not attempted"
        | Delivery.Next _ -> MergeAuthorized

    /// Invoke the merge adapter only after claim, head, and effective base are re-read and still match.
    let guardedLanding freshnessToken actionKey facts currentClaimGeneration currentHead currentBase merge =
        match authorizeGuardedLanding freshnessToken actionKey facts currentClaimGeneration with
        | MergeRefused reason -> Error reason
        | MergeAuthorized when currentHead <> Some facts.Freshness.HeadSha ->
            Error "delivery PR head changed after inspection; GitHub merge was not attempted"
        | MergeAuthorized ->
            let acceptedBase = facts.Review |> Option.bind _.BaseSha
            match acceptedBase, currentBase with
            | Some expected, Some actual when expected = actual ->
                Ok { HeadSha = facts.Freshness.HeadSha; BaseSha = actual; Result = merge () }
            | Some expected, Some actual ->
                Error $"delivery effective base changed after acceptance: expected %s{expected}, actual %s{actual}; GitHub merge was not attempted"
            | None, _ -> Error "delivery accepted review carries no effective base SHA; GitHub merge was not attempted"
            | _, None -> Error "delivery effective base could not be re-read; GitHub merge was not attempted"

    let private snapshot (raw: string) : Result<Delivery.Snapshot, string> =
        try
            use document = JsonDocument.Parse raw
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then invalidArg "snapshot" "must be an object"
            let freshnessElement = required "freshness" root
            if freshnessElement.ValueKind <> JsonValueKind.Object then invalidArg "freshness" "must be an object"
            let freshness: Delivery.Freshness =
                { ItemRef = readString "itemRef" freshnessElement
                  ClaimGeneration = readString "claimGeneration" freshnessElement
                  Executor = readString "executor" freshnessElement
                  Branch = readString "branch" freshnessElement
                  Worktree = readString "worktree" freshnessElement
                  PullRequest = readOptionalInteger "pullRequest" freshnessElement
                  HeadSha = readString "headSha" freshnessElement
                  DeclaredPaths = declaredPaths freshnessElement
                  BoardState = readString "boardState" freshnessElement }
            Ok
                { Freshness = freshness
                  ItemBranchCanonical = readBoolean "itemBranchCanonical" root
                  ClosingLinkageCanonical = readBoolean "closingLinkageCanonical" root
                  PathsVerified = readBoolean "pathsVerified" root
                  InReview = readBoolean "inReview" root
                  Review = review root
                  // `reviewProblem` was added after the first delivery snapshot contract shipped.
                  // Older producers omit it, which means no parser failure was observed; accepting
                  // that shape as None preserves the pure adapter's established wire contract.
                  ReviewProblem = readOptionalStringOrAbsent "reviewProblem" root
                  Landable = readBoolean "landable" root
                  Merged = readBoolean "merged" root
                  MergeReachable = readBoolean "mergeReachable" root
                  IssueClosed = readBoolean "issueClosed" root
                  BoardDone = readBoolean "boardDone" root
                  ClaimReleased = readBoolean "claimReleased" root
                  PendingWrites = readInteger "pendingWrites" root
                  CleanupEligible = readBoolean "cleanupEligible" root
                  ObligationsDeclared = readBoolean "obligationsDeclared" root
                  Obligations = obligations root
                  ParkedReason = readOptionalString "parkedReason" root }
        with error -> Error error.Message

    let private stage (value: Delivery.Stage) =
        match value with
        | Delivery.Claimed -> "claimed"
        | Delivery.Implementation -> "implementation"
        | Delivery.ReviewReady -> "reviewReady"
        | Delivery.ReviewActive -> "reviewActive"
        | Delivery.Accepted -> "accepted"
        | Delivery.Landable -> "landable"
        | Delivery.MergedAwaitingObligations -> "mergedAwaitingObligations"
        | Delivery.Done -> "done"
        | Delivery.Parked -> "parked"

    let private action (value: Delivery.Action) =
        match value with
        | Delivery.ContinueImplementation -> "continueImplementation"
        | Delivery.RepairReviewHandoff _ -> "repairReviewHandoff"
        | Delivery.MoveToReview -> "moveToReview"
        | Delivery.AwaitIndependentReview -> "awaitIndependentReview"
        | Delivery.RefreshReview _ -> "refreshReview"
        | Delivery.AwaitLandability -> "awaitLandability"
        | Delivery.GuardedLand -> "guardedLand"
        | Delivery.VerifyObligation _ -> "verifyObligation"
        | Delivery.Complete -> "complete"
        | Delivery.CleanupWorktree -> "cleanupWorktree"
        | Delivery.RouteFollowUp _ -> "routeFollowUp"

    let private actionProblem (value: Delivery.Action) =
        match value with
        | Delivery.RepairReviewHandoff reason
        | Delivery.RefreshReview reason
        | Delivery.RouteFollowUp reason -> Some reason
        | _ -> None

    let render opts facts =
        match Delivery.inspect facts with
        | Delivery.NoVerdict reason ->
            match opts.Render with
            | Json -> printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery/1"; verdict = "noVerdict"; reason = reason |})
            | Text -> eprint $"UNDETERMINED — %s{reason}"
            ExitCode.toInt ExitCode.NoVerdict
        | Delivery.Next transition ->
            match opts.Render with
            | Json ->
                printfn "%s" (JsonSerializer.Serialize {| schema = "fsgg.coord.delivery/1"; verdict = "next"; stage = stage transition.Stage; action = action transition.Action; problem = actionProblem transition.Action; freshnessToken = transition.FreshnessToken; actionKey = transition.ActionKey |})
            | Text ->
                match actionProblem transition.Action with
                | Some problem -> printfn "%s — %s: %s" (stage transition.Stage) (action transition.Action) problem
                | None -> printfn "%s — %s" (stage transition.Stage) (action transition.Action)
            ExitCode.toInt ExitCode.Green

    let run opts =
        let raw = input opts
        if String.IsNullOrWhiteSpace raw then
            eprint "fsgg-coord-engine: delivery snapshot is empty; refusing to infer lifecycle state."
            ExitCode.toInt ExitCode.Error
        else
            match snapshot raw with
            | Error error ->
                eprint $"fsgg-coord-engine: delivery snapshot is malformed: %s{error}"
                ExitCode.toInt ExitCode.Error
            | Ok facts -> render opts facts
