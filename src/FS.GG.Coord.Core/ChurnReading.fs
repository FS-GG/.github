namespace FS.GG.Coord

open System
open System.Globalization
open System.Text.Json

module ChurnReading =
    [<Literal>]
    let Schema = "fsgg.coord.churn-reading/v1"

    [<Literal>]
    let ResultSchema = "fsgg.coord.churn-reading-result/v1"

    type Census<'a> =
        | Found of 'a list
        | SearchedNotFound of string
        | NotSearched of string

    type CauseInstance = { Cause: string; Rows: string list }

    type DerivedRow = { Row: string; Gate: string }

    type JointlyRed = { Rows: string list; Gate: string }

    type Window = { Start: DateTimeOffset; End: DateTimeOffset }

    type Landed = { Count: int; Unit: string }

    type Reading =
        { Schema: string
          Window: Window
          RowsOpened: int
          RowsClosed: int
          NetRowDelta: int
          ItemsLanded: Landed
          CauseInstances: Census<CauseInstance>
          SuccessorGeneratingCauses: Census<CauseInstance>
          AlreadyDerived: Census<DerivedRow>
          JointlyRedCandidates: Census<JointlyRed>
          Remedy: string option
          Prose: string }

    type Finding = { Field: string; Detail: string }

    let private rootFields =
        [ "schema"
          "window"
          "rowsOpened"
          "rowsClosed"
          "netRowDelta"
          "itemsLanded"
          "causeInstances"
          "successorGeneratingCauses"
          "alreadyDerived"
          "jointlyRedCandidates"
          "remedy"
          "prose" ]

    // Said once, so every census field says it the same way.
    let private censusShape =
        "must be an object carrying exactly one of 'found' (the pass looked and these are the rows; a "
        + "non-empty array), 'searchedNotFound' (the pass looked and there are none; the string is the "
        + "search performed) or 'notSearched' (the pass did not look; the string is why not)"

    let private collapses =
        "cannot distinguish 'the pass looked and found none' from 'the pass did not look', and making "
        + "exactly that distinction visible is what this schema is for"

    let private instantFormats =
        [| "yyyy-MM-dd'T'HH:mm:ss'Z'"; "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" |]

    let private instantShape =
        "must be an explicit UTC instant such as '2026-08-14T17:00:00Z'; 'today', 'this session' and a "
        + "bare date are not window endpoints"

    // AssumeUniversal ||| AdjustToUniversal, with 'Z' as a QUOTED LITERAL in both formats, so the
    // decoded offset is zero on every machine. A style that let the host's local zone decide would make
    // this validator's verdict depend on where it ran, which is the class of defect it exists to catch.
    let private parseInstant (raw: string) =
        match
            DateTimeOffset.TryParseExact(
                raw,
                instantFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal
            )
        with
        | true, value -> Some value
        | _ -> None

    let private qualify (prefix: string) (name: string) =
        if prefix = "" then name else $"%s{prefix}.%s{name}"

    let private errorsOf (results: Result<'a, Finding list> list) =
        results
        |> List.collect (function
            | Error findings -> findings
            | Ok _ -> [])

    let private valuesOf (results: Result<'a, Finding list> list) =
        results
        |> List.choose (function
            | Ok value -> Some value
            | Error _ -> None)

    let private unknownFields (prefix: string) (known: string list) (element: JsonElement) =
        let allowed = Set.ofList known
        let where = if prefix = "" then "the document" else prefix

        element.EnumerateObject()
        |> Seq.map (fun property -> property.Name)
        |> Seq.filter (allowed.Contains >> not)
        |> Seq.map (fun name ->
            { Field = qualify prefix name
              Detail = $"is not a field of {Schema}; the fields of %s{where} are " + String.concat ", " known })
        |> Seq.toList

    let private requiredStringIn (prefix: string) (element: JsonElement) (name: string) =
        let field = qualify prefix name

        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | true, value when value.ValueKind = JsonValueKind.Null ->
            Error [ { Field = field; Detail = "is required and must be a string, but is null" } ]
        | true, _ -> Error [ { Field = field; Detail = "must be a string" } ]
        | false, _ -> Error [ { Field = field; Detail = "is required" } ]

    let private requiredIntIn (prefix: string) (element: JsonElement) (name: string) =
        let field = qualify prefix name

        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Number ->
            match value.TryGetInt32() with
            | true, number -> Ok number
            | _ -> Error [ { Field = field; Detail = "must be a whole number" } ]
        | true, _ -> Error [ { Field = field; Detail = "must be a whole number" } ]
        | false, _ -> Error [ { Field = field; Detail = "is required" } ]

    let private stringArrayIn (prefix: string) (element: JsonElement) (name: string) =
        let field = qualify prefix name

        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Array ->
            let items = value.EnumerateArray() |> Seq.toList

            if items |> List.forall (fun item -> item.ValueKind = JsonValueKind.String) then
                Ok(items |> List.map (fun item -> item.GetString()))
            else
                Error [ { Field = field; Detail = "must be an array of strings" } ]
        | true, _ -> Error [ { Field = field; Detail = "must be an array of strings" } ]
        | false, _ -> Error [ { Field = field; Detail = "is required" } ]

    let private requiredObject (root: JsonElement) (name: string) =
        match root.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.Object -> Ok value
        | true, _ -> Error [ { Field = name; Detail = "must be an object" } ]
        | false, _ -> Error [ { Field = name; Detail = "is required" } ]

    let private decodeWindow (root: JsonElement) =
        match requiredObject root "window" with
        | Error findings -> Error findings
        | Ok element ->
            let unknown = unknownFields "window" [ "start"; "end" ] element

            if not (List.isEmpty unknown) then
                Error unknown
            else

            let endpoint name =
                let field = qualify "window" name

                match element.TryGetProperty name with
                | true, value when value.ValueKind = JsonValueKind.String ->
                    match parseInstant (value.GetString()) with
                    | Some instant -> Ok instant
                    | None -> Error [ { Field = field; Detail = instantShape } ]
                | true, _ -> Error [ { Field = field; Detail = instantShape } ]
                | false, _ -> Error [ { Field = field; Detail = $"is required and %s{instantShape}" } ]

            let start = endpoint "start"
            let finish = endpoint "end"

            match start, finish with
            | Ok start, Ok finish -> Ok { Start = start; End = finish }
            | _ -> Error(errorsOf [ start; finish ])

    let private decodeLanded (root: JsonElement) =
        match requiredObject root "itemsLanded" with
        | Error findings -> Error findings
        | Ok element ->
            let unknown = unknownFields "itemsLanded" [ "count"; "unit" ] element

            if not (List.isEmpty unknown) then
                Error unknown
            else

            let count = requiredIntIn "itemsLanded" element "count"
            let unit = requiredStringIn "itemsLanded" element "unit"

            match count, unit with
            | Ok count, Ok unit -> Ok { Count = count; Unit = unit }
            | _ -> Error(errorsOf [ count ] @ errorsOf [ unit ])

    let private decodeCauseInstance (path: string) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            Error [ { Field = path; Detail = "must be an object carrying 'cause' and 'rows'" } ]
        else

        let unknown = unknownFields path [ "cause"; "rows" ] element

        if not (List.isEmpty unknown) then
            Error unknown
        else

        let cause = requiredStringIn path element "cause"
        let rows = stringArrayIn path element "rows"

        match cause, rows with
        | Ok cause, Ok rows -> Ok { Cause = cause; Rows = rows }
        | _ -> Error(errorsOf [ cause ] @ errorsOf [ rows ])

    let private decodeDerivedRow (path: string) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            Error [ { Field = path; Detail = "must be an object carrying 'row' and 'gate'" } ]
        else

        let unknown = unknownFields path [ "row"; "gate" ] element

        if not (List.isEmpty unknown) then
            Error unknown
        else

        let row = requiredStringIn path element "row"
        let gate = requiredStringIn path element "gate"

        match row, gate with
        | Ok row, Ok gate -> Ok { Row = row; Gate = gate }
        | _ -> Error(errorsOf [ row; gate ])

    let private decodeJointlyRed (path: string) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            Error [ { Field = path; Detail = "must be an object carrying 'rows' and 'gate'" } ]
        else

        let unknown = unknownFields path [ "rows"; "gate" ] element

        if not (List.isEmpty unknown) then
            Error unknown
        else

        let rows = stringArrayIn path element "rows"
        let gate = requiredStringIn path element "gate"

        match rows, gate with
        | Ok rows, Ok gate -> Ok { Rows = rows; Gate = gate }
        | _ -> Error(errorsOf [ rows ] @ errorsOf [ gate ])

    let private decodeCensus
        (field: string)
        (decodeItem: string -> JsonElement -> Result<'a, Finding list>)
        (root: JsonElement)
        : Result<Census<'a>, Finding list> =
        let malformed detail =
            Error [ { Field = field; Detail = $"%s{detail}; it %s{censusShape}" } ]

        match root.TryGetProperty field with
        | false, _ -> Error [ { Field = field; Detail = $"is required and %s{censusShape}" } ]
        | true, element ->
            match element.ValueKind with
            | JsonValueKind.String -> malformed $"is the string '%s{element.GetString()}', which %s{collapses}"
            | JsonValueKind.Null -> malformed $"is null, which %s{collapses}"
            | JsonValueKind.Array -> malformed $"is a bare array, which %s{collapses}"
            | JsonValueKind.Object ->
                let properties = element.EnumerateObject() |> Seq.toList

                match properties with
                | [ property ] ->
                    let payload = property.Value

                    let stringCase construct =
                        if payload.ValueKind = JsonValueKind.String then
                            Ok(construct (payload.GetString()))
                        else
                            malformed $"'%s{property.Name}' must carry a string"

                    match property.Name with
                    | "found" ->
                        if payload.ValueKind <> JsonValueKind.Array then
                            malformed "'found' must carry an array"
                        else
                            let decoded =
                                payload.EnumerateArray()
                                |> Seq.toList
                                |> List.mapi (fun index item -> decodeItem $"%s{field}.found[%d{index}]" item)

                            let failures = errorsOf decoded

                            if List.isEmpty failures then
                                Ok(Found(valuesOf decoded))
                            else
                                Error failures
                    | "searchedNotFound" -> stringCase SearchedNotFound
                    | "notSearched" -> stringCase NotSearched
                    | other -> malformed $"names the unknown case '%s{other}'"
                | [] -> malformed "is an empty object"
                | _ ->
                    let named = properties |> List.map (fun property -> property.Name) |> String.concat ", "
                    malformed $"names more than one case (%s{named})"
            | other -> malformed $"is a JSON %s{other.ToString().ToLowerInvariant()}"

    let private decodeRemedy (root: JsonElement) =
        match root.TryGetProperty "remedy" with
        | true, value when value.ValueKind = JsonValueKind.String -> Ok(Some(value.GetString()))
        | true, value when value.ValueKind = JsonValueKind.Null -> Ok None
        | true, _ ->
            Error
                [ { Field = "remedy"
                    Detail = "must be a string, or explicit null when no pathology is present" } ]
        | false, _ ->
            Error
                [ { Field = "remedy"
                    Detail =
                      "is required; write explicit null when no pathology is present, never the string 'none' "
                      + "and never an absent key" } ]

    let private isPathology (census: Census<'a>) =
        match census with
        | Found items -> not (List.isEmpty items)
        | SearchedNotFound _
        | NotSearched _ -> false

    let private blankRows (field: string) (index: int) (label: string) (rows: string list) =
        [ for rowIndex, row in List.indexed rows do
              if String.IsNullOrWhiteSpace row then
                  yield
                      { Field = $"%s{field}.found[%d{index}].%s{label}[%d{rowIndex}]"
                        Detail = "must be a row reference" } ]

    let private causeInstanceProblems (field: string) (index: int) (item: CauseInstance) =
        [ if String.IsNullOrWhiteSpace item.Cause then
              yield
                  { Field = $"%s{field}.found[%d{index}].cause"
                    Detail = "must name the cause in the analyst's own words" }

          if List.isEmpty item.Rows then
              yield
                  { Field = $"%s{field}.found[%d{index}].rows"
                    Detail = "must name at least one row" }

          yield! blankRows field index "rows" item.Rows ]

    let private derivedRowProblems (field: string) (index: int) (item: DerivedRow) =
        [ if String.IsNullOrWhiteSpace item.Row then
              yield { Field = $"%s{field}.found[%d{index}].row"; Detail = "must name the row" }

          if String.IsNullOrWhiteSpace item.Gate then
              yield
                  { Field = $"%s{field}.found[%d{index}].gate"
                    Detail = "must name the scripts/check-*.py that already derives the condition" } ]

    let private jointlyRedProblems (field: string) (index: int) (item: JointlyRed) =
        [ // "Individually green and JOINTLY red" is a coupling between rows. One row cannot be jointly
          // anything, so a single-row entry is a different finding wearing this element's shape.
          if List.length item.Rows < 2 then
              yield
                  { Field = $"%s{field}.found[%d{index}].rows"
                    Detail =
                      "must name at least two rows: this element is a COUPLING — rows individually green "
                      + "and jointly red through a gate corpus neither author controls" }

          yield! blankRows field index "rows" item.Rows

          if String.IsNullOrWhiteSpace item.Gate then
              yield
                  { Field = $"%s{field}.found[%d{index}].gate"
                    Detail = "must name the shared gate corpus the rows are jointly red through" } ]

    let private censusProblems
        (field: string)
        (element: string)
        (itemProblems: string -> int -> 'a -> Finding list)
        (census: Census<'a>)
        =
        match census with
        | Found [] ->
            [ { Field = field
                Detail =
                  "'found' must carry at least one row; a pass that looked and found none says so with "
                  + "'searchedNotFound', which carries the search performed" } ]
        | Found items -> items |> List.mapi (itemProblems field) |> List.concat
        | SearchedNotFound search when String.IsNullOrWhiteSpace search ->
            [ { Field = field
                Detail =
                  "'searchedNotFound' must carry the search performed, so a reader can check the claim "
                  + "rather than trust it" } ]
        | SearchedNotFound _ -> []
        | NotSearched why when String.IsNullOrWhiteSpace why ->
            [ { Field = field; Detail = "'notSearched' must carry why the search was not performed" } ]
        | NotSearched why ->
            [ { Field = field
                Detail =
                  $"is 'notSearched' (%s{why}). %s{element} is one of the six REQUIRED elements of a churn "
                  + "reading, so this pass is INCOMPLETE rather than clean: perform the search, or record "
                  + "what you did look for with 'searchedNotFound'" } ]

    let private elementTwo = "Element 2 (rows that are instances of one cause)"
    let private elementThree = "Element 3 (cause rows whose repairs keep generating successors)"

    let private elementFour =
        "Element 4 (rows restating a condition a scripts/check-*.py already derives)"

    let private elementSix =
        "Element 6 (rows individually green and jointly red through a shared gate corpus)"

    let validate (now: DateTimeOffset) (reading: Reading) =
        let findings =
            [ if reading.Schema <> Schema then
                  yield { Field = "schema"; Detail = $"must be '{Schema}'" }

              if reading.Window.End <= reading.Window.Start then
                  yield
                      { Field = "window"
                        Detail = "must be a closed interval whose end is strictly after its start" }

              if reading.Window.End > now then
                  yield
                      { Field = "window.end"
                        Detail =
                          "must already have happened; a reading whose window has not closed cannot be "
                          + "re-derived even by the person who wrote it, five minutes later" }

              if reading.RowsOpened < 0 then
                  yield { Field = "rowsOpened"; Detail = "cannot be negative" }

              if reading.RowsClosed < 0 then
                  yield { Field = "rowsClosed"; Detail = "cannot be negative" }

              if reading.NetRowDelta <> reading.RowsOpened - reading.RowsClosed then
                  yield
                      { Field = "netRowDelta"
                        Detail =
                          $"is %d{reading.NetRowDelta}, but rowsOpened %d{reading.RowsOpened} minus rowsClosed "
                          + $"%d{reading.RowsClosed} is %d{reading.RowsOpened - reading.RowsClosed}" }

              if reading.ItemsLanded.Count < 0 then
                  yield { Field = "itemsLanded.count"; Detail = "cannot be negative" }

              if String.IsNullOrWhiteSpace reading.ItemsLanded.Unit then
                  yield
                      { Field = "itemsLanded.unit"
                        Detail =
                          "must name what 'landed' counts — 'issues closed', 'pull requests merged' — because "
                          + "a count whose unit is unrecoverable cannot be re-derived under any reading of it" }

              yield! censusProblems "causeInstances" elementTwo causeInstanceProblems reading.CauseInstances

              yield!
                  censusProblems
                      "successorGeneratingCauses"
                      elementThree
                      causeInstanceProblems
                      reading.SuccessorGeneratingCauses

              yield! censusProblems "alreadyDerived" elementFour derivedRowProblems reading.AlreadyDerived

              yield!
                  censusProblems "jointlyRedCandidates" elementSix jointlyRedProblems reading.JointlyRedCandidates

              if String.IsNullOrWhiteSpace reading.Prose then
                  yield
                      { Field = "prose"
                        Detail =
                          "is required: this document is the checkable skeleton BESIDE the reading, never a "
                          + "replacement for it, and the reading's value is the judgement in it" }

              match reading.Remedy with
              | Some remedy when String.IsNullOrWhiteSpace remedy ->
                  yield
                      { Field = "remedy"
                        Detail = "must carry the mechanism, or be explicit null when no pathology is present" }
              | Some remedy when remedy.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ->
                  yield
                      { Field = "remedy"
                        Detail =
                          "is the string 'none', which is the sentinel this schema replaces; write explicit null" }
              | _ -> ()

              let pathologies =
                  [ "causeInstances", isPathology reading.CauseInstances
                    "successorGeneratingCauses", isPathology reading.SuccessorGeneratingCauses
                    "alreadyDerived", isPathology reading.AlreadyDerived
                    "jointlyRedCandidates", isPathology reading.JointlyRedCandidates ]
                  |> List.filter snd
                  |> List.map fst

              if not (List.isEmpty pathologies) && Option.isNone reading.Remedy then
                  yield
                      { Field = "remedy"
                        Detail =
                          "is required to be non-null whenever a pathology is present, and "
                          + String.concat ", " pathologies
                          + " report found rows; a remedy names a mechanism, not an intention" } ]

        if List.isEmpty findings then Ok reading else Error findings

    let parse (document: string) =
        try
            use parsed = JsonDocument.Parse document
            let root = parsed.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                Error [ { Field = "document"; Detail = $"must be a JSON object carrying a {Schema} reading" } ]
            else

            let unknown = unknownFields "" rootFields root

            if not (List.isEmpty unknown) then
                Error unknown
            else

            let schema = requiredStringIn "" root "schema"
            let prose = requiredStringIn "" root "prose"
            let window = decodeWindow root
            let rowsOpened = requiredIntIn "" root "rowsOpened"
            let rowsClosed = requiredIntIn "" root "rowsClosed"
            let netRowDelta = requiredIntIn "" root "netRowDelta"
            let itemsLanded = decodeLanded root
            let causeInstances = decodeCensus "causeInstances" decodeCauseInstance root
            let successors = decodeCensus "successorGeneratingCauses" decodeCauseInstance root
            let alreadyDerived = decodeCensus "alreadyDerived" decodeDerivedRow root
            let jointlyRed = decodeCensus "jointlyRedCandidates" decodeJointlyRed root
            let remedy = decodeRemedy root

            match
                schema, prose, window, rowsOpened, rowsClosed, netRowDelta, itemsLanded, causeInstances, successors,
                alreadyDerived, jointlyRed, remedy
            with
            | Ok schema,
              Ok prose,
              Ok window,
              Ok rowsOpened,
              Ok rowsClosed,
              Ok netRowDelta,
              Ok itemsLanded,
              Ok causeInstances,
              Ok successors,
              Ok alreadyDerived,
              Ok jointlyRed,
              Ok remedy ->
                Ok
                    { Schema = schema
                      Window = window
                      RowsOpened = rowsOpened
                      RowsClosed = rowsClosed
                      NetRowDelta = netRowDelta
                      ItemsLanded = itemsLanded
                      CauseInstances = causeInstances
                      SuccessorGeneratingCauses = successors
                      AlreadyDerived = alreadyDerived
                      JointlyRedCandidates = jointlyRed
                      Remedy = remedy
                      Prose = prose }
            | _ ->
                Error(
                    errorsOf [ schema; prose ]
                    @ errorsOf [ window ]
                    @ errorsOf [ rowsOpened; rowsClosed; netRowDelta ]
                    @ errorsOf [ itemsLanded ]
                    @ errorsOf [ causeInstances; successors ]
                    @ errorsOf [ alreadyDerived ]
                    @ errorsOf [ jointlyRed ]
                    @ errorsOf [ remedy ]
                )
        with :? JsonException as ex ->
            Error [ { Field = "document"; Detail = $"is not valid JSON: %s{ex.Message}" } ]

    let private censusReport (census: Census<'a>) =
        match census with
        | Found items -> $"{{\"case\":\"found\",\"count\":%d{List.length items}}}"
        | SearchedNotFound _ -> "{\"case\":\"searchedNotFound\",\"count\":0}"
        | NotSearched _ -> "{\"case\":\"notSearched\",\"count\":0}"

    let renderResult (reading: Reading) =
        let text (value: string) = JsonSerializer.Serialize value

        let instant (value: DateTimeOffset) =
            text (value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))

        String.concat
            ""
            [ "{\"schema\":\""
              ResultSchema
              "\",\"kind\":\"validated\",\"window\":{\"start\":"
              instant reading.Window.Start
              ",\"end\":"
              instant reading.Window.End
              "},\"rowsOpened\":"
              string reading.RowsOpened
              ",\"rowsClosed\":"
              string reading.RowsClosed
              ",\"netRowDelta\":"
              string reading.NetRowDelta
              ",\"itemsLanded\":{\"count\":"
              string reading.ItemsLanded.Count
              ",\"unit\":"
              text reading.ItemsLanded.Unit
              "},\"causeInstances\":"
              censusReport reading.CauseInstances
              ",\"successorGeneratingCauses\":"
              censusReport reading.SuccessorGeneratingCauses
              ",\"alreadyDerived\":"
              censusReport reading.AlreadyDerived
              ",\"jointlyRedCandidates\":"
              censusReport reading.JointlyRedCandidates
              ",\"remedy\":\""
              (match reading.Remedy with
               | Some _ -> "present"
               | None -> "null")
              "\",\"writes\":0}" ]
