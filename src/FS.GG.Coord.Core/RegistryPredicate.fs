namespace FS.GG.Coord

module RegistryPredicate =

    type Row =
        { Id: string
          Owner: string option
          Source: string option
          Fields: Map<string, string> }

    type OwnerDeclaration =
        | Declares of value: string
        | Silent
        | Unreadable of reason: string

    type Assertion =
        { Id: string
          Field: string
          Value: string }

    type Verdict =
        | Agrees
        | Contradicts of ownerValue: string * note: string
        | Unknown of reason: string

    let assertionLabels = ("Asserted registry id", "Asserted registry field", "Asserted registry value")

    // ---- parsing registry/skills.yml rows ---------------------------------------------------------

    /// Split the inside of a `{ ... }` flow-mapping on its TOP-LEVEL commas — commas inside a `"..."`
    /// string literal or a `[...]` list are part of a value (`materializes-when: "profile in [a, b]"`),
    /// not separators. Same reason `normalize_when` stashes literals: a comma is layout only outside them.
    let private splitTopLevel (inside: string) : string list =
        let parts = System.Collections.Generic.List<string>()
        let sb = System.Text.StringBuilder()
        let mutable depth = 0
        let mutable inStr = false
        for ch in inside do
            match ch with
            | '"' ->
                inStr <- not inStr
                sb.Append(ch) |> ignore
            | '[' when not inStr ->
                depth <- depth + 1
                sb.Append(ch) |> ignore
            | ']' when not inStr ->
                depth <- max 0 (depth - 1)
                sb.Append(ch) |> ignore
            | ',' when not inStr && depth = 0 ->
                parts.Add(sb.ToString())
                sb.Clear() |> ignore
            | _ -> sb.Append(ch) |> ignore
        parts.Add(sb.ToString())
        parts |> List.ofSeq

    let private unquote (v: string) : string =
        let t = v.Trim()
        if t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"") then t.Substring(1, t.Length - 2)
        else t

    /// Parse one `- { k: v, ... }` line into a field map, or `None` if the line is not a flow-mapping
    /// row (a comment, the `skills:` header, a blank line).
    let private parseRow (line: string) : Row option =
        let t = line.Trim()
        if not (t.StartsWith("-")) then
            None
        else
            let afterDash = t.Substring(1).Trim()
            let lb = afterDash.IndexOf('{')
            let rb = afterDash.LastIndexOf('}')
            if lb < 0 || rb <= lb then
                None
            else
                let inside = afterDash.Substring(lb + 1, rb - lb - 1)
                let fields =
                    splitTopLevel inside
                    |> List.choose (fun token ->
                        let token = token.Trim()
                        if token = "" then
                            None
                        else
                            let colon = token.IndexOf(':')
                            if colon <= 0 then None
                            else
                                let key = token.Substring(0, colon).Trim()
                                let value = unquote (token.Substring(colon + 1))
                                Some(key, value))
                    |> Map.ofList
                match Map.tryFind "id" fields with
                | Some id when id <> "" ->
                    Some
                        { Id = id
                          Owner = Map.tryFind "owner" fields
                          Source = Map.tryFind "source" fields
                          Fields = fields }
                | _ -> None

    let parseRows (yaml: string) : Row list =
        yaml.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.choose parseRow

    let findRow (rows: Row list) (id: string) : Row option =
        rows |> List.tryFind (fun r -> r.Id = id)

    // ---- parsing the cross-repo-request assertion -------------------------------------------------

    /// The value of a `### <label>` section of a rendered issue-form body, or `None` when the section
    /// is absent or was left blank (`_No response_`).
    let private sectionValue (issueBody: string) (label: string) : string option =
        let lines = issueBody.Replace("\r\n", "\n").Split('\n')
        let heading = "### " + label
        let mutable i = 0
        let mutable found = -1
        while found < 0 && i < lines.Length do
            if lines.[i].Trim() = heading then found <- i
            i <- i + 1
        if found < 0 then
            None
        else
            let collected =
                lines
                |> Array.skip (found + 1)
                |> Array.takeWhile (fun l -> not (l.TrimStart().StartsWith("### ")))
                |> Array.map (fun l -> l.Trim())
                |> Array.filter (fun l -> l <> "")
            match collected with
            | [||] -> None
            | _ ->
                let value = String.concat " " (List.ofArray collected)
                if value.Trim().ToLowerInvariant() = "_no response_" then None else Some(value.Trim())

    let parseAssertion (issueBody: string) : Assertion option =
        let (idLabel, fieldLabel, valueLabel) = assertionLabels
        match sectionValue issueBody idLabel, sectionValue issueBody fieldLabel, sectionValue issueBody valueLabel with
        | Some id, Some field, Some value -> Some { Id = id; Field = field; Value = value }
        | _ -> None

    // ---- the oracle -------------------------------------------------------------------------------

    // Only `mirrored` is compared today: a bool, absent ⇒ Unknown (`mirror_of` / .github#658). Every
    // other field abstains rather than risk a rule that drifts from the reconcile (ADR-0050).
    let supportsField (field: string) : bool =
        field.Trim().ToLowerInvariant() = "mirrored"

    /// Normalise a `mirrored` value for comparison: trimmed, lower-cased. The Cli hands the owner's
    /// value in as `true`/`false` (a JSON bool rendered), and a request's value is free text.
    let private normalizeMirrored (v: string) : string = v.Trim().ToLowerInvariant()

    let private valuesAgree (field: string) (ownerValue: string) (requested: string) : bool =
        // supportsField has already restricted this to `mirrored`.
        ignore field
        normalizeMirrored ownerValue = normalizeMirrored requested

    let private governingNote (field: string) (ownerSlug: string) (ownerValue: string) : string =
        // supportsField restricts to `mirrored`.
        ignore field
        sprintf
            "`mirrored:` is ADR-0022 §6's frozen-mirror OBLIGATION, and only the owner (`%s`) declares it — an absent value is UNKNOWN, never `false` (.github#658). The owning manifest declares `mirrored: %s`; a request asserting otherwise is the FS.GG.Rendering#505 / .github#658 trap the field exists to refuse. Derive from the owner, not from this projection (ADR-0044)."
            ownerSlug ownerValue

    let classify (rows: Row list) (owner: OwnerDeclaration) (assertion: Assertion) : Verdict =
        match findRow rows assertion.Id with
        | None ->
            Unknown(
                sprintf
                    "no `registry/skills.yml` row for `%s` — an assertion about a skill the registry does not carry cannot be verified against an owner"
                    assertion.Id
            )
        | Some row ->
            if not (supportsField assertion.Field) then
                Unknown(
                    sprintf
                        "field `%s` is not one the oracle compares faithfully — only `mirrored` today; comparing it exactly needs the canonical `normalize_when` seam (.github#398), and a second rule here is the divergence ADR-0050 forbids"
                        assertion.Field
                )
            else
                match owner with
                | Unreadable reason ->
                    Unknown(sprintf "the owning manifest for `%s` could not be read: %s" assertion.Id reason)
                | Silent ->
                    let ownerSlug = row.Owner |> Option.defaultValue "the owning producer"
                    Unknown(
                        sprintf
                            "%s declares no `%s` for `%s` — an absent value is UNKNOWN, never a refutation (`mirror_of` / .github#658)"
                            ownerSlug
                            assertion.Field
                            assertion.Id
                    )
                | Declares ownerValue ->
                    if valuesAgree assertion.Field ownerValue assertion.Value then
                        Agrees
                    else
                        let ownerSlug = row.Owner |> Option.defaultValue "the owning producer"
                        Contradicts(ownerValue, governingNote assertion.Field ownerSlug ownerValue)

    let name (verdict: Verdict) : string =
        match verdict with
        | Agrees -> "agrees"
        | Contradicts _ -> "contradicts"
        | Unknown _ -> "unknown"
