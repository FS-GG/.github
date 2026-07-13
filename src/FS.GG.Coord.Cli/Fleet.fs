namespace FS.GG.Coord.Cli

module Fleet =

    open System
    open System.IO
    open System.Globalization
    open System.Text.Json
    open FS.GG.Coord
    open FS.GG.Coord.Types
    open FS.GG.Coord.Cli.Json

    [<Literal>]
    let private LedgerSchema = "fsgg.coord.ledger/1"

    [<Literal>]
    let private VerdictSchema = "fsgg.coord.fleet/1"

    type Error = Json.Error

    type Query =
        { Engine: string
          RequiredDays: int
          MinWorkers: int
          Today: DateOnly
          Reports: Divergence.Report list }

    // ================================================================================================
    // READING
    // ================================================================================================

    /// ISO-8601 calendar days, and ONLY those. `DateOnly.Parse` would happily read `07/13/2026` under
    /// one culture and `13/07/2026` under another — a ledger whose meaning depends on the locale of the
    /// worker that reads it is not a ledger. Exact, invariant, or an error.
    let private asDay (path: string) (el: JsonElement) : Result<DateOnly, Error list> =
        match asString path el with
        | Error e -> Error e
        | Ok s ->
            match DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, d -> Ok d
            | _ -> err path $"expected a UTC calendar day as yyyy-MM-dd, got '%s{s}'"

    /// A count that cannot be negative. See the signature file: a negative count is a broken publisher,
    /// and clamping it to zero would disguise that as an uncovered day.
    let private countField (path: string) (name: string) (el: JsonElement) : Result<int, Error list> =
        match intField path name el with
        | Error e -> Error e
        | Ok n when n < 0 -> err $"%s{path}.%s{name}" $"a count may not be negative, got %d{n}"
        | Ok n -> Ok n

    let private nonEmpty (path: string) (name: string) (el: JsonElement) : Result<string, Error list> =
        match stringField path name el with
        | Error e -> Error e
        | Ok s when String.IsNullOrWhiteSpace s -> err $"%s{path}.%s{name}" "may not be blank"
        | Ok s -> Ok(s.Trim())

    let private report (i: int) (el: JsonElement) : Result<Divergence.Report, Error list> =
        let path = $"$.reports[%d{i}]"

        let worker = nonEmpty path "worker" el
        let day = prop path "day" el |> Result.bind (asDay $"%s{path}.day")
        let engine = nonEmpty path "engine" el
        let ran = countField path "ran" el
        let skipped = countField path "skipped" el
        let compared = countField path "compared" el
        let outcome = countField path "outcome" el
        let unpaired = countField path "unpaired" el
        let refused = countField path "engineRed" el
        let reason = countField path "reason" el

        let errors =
            [ (worker |> Result.map ignore)
              (day |> Result.map ignore)
              (engine |> Result.map ignore)
              (ran |> Result.map ignore)
              (skipped |> Result.map ignore)
              (compared |> Result.map ignore)
              (outcome |> Result.map ignore)
              (unpaired |> Result.map ignore)
              (refused |> Result.map ignore)
              (reason |> Result.map ignore) ]
            |> List.collect (
                function
                | Error e -> e
                | Ok _ -> []
            )

        if not (List.isEmpty errors) then
            Error errors
        else

        match worker, day, engine, ran, skipped, compared, outcome, unpaired, refused, reason with
        | Ok w, Ok d, Ok eng, Ok rn, Ok sk, Ok cmp, Ok out, Ok unp, Ok ref, Ok rsn ->
            Ok
                { Worker = WorkerId w
                  Day = d
                  Engine = eng
                  Ran = rn
                  Skipped = sk
                  Compared = cmp
                  OutcomeDivergences = out
                  Unpaired = unp
                  EngineRefused = ref
                  ReasonDivergences = rsn }
        | _ -> Error errors

    let parse (json: string) : Result<Query, Error list> =
        let doc =
            try
                Ok(JsonDocument.Parse(json: string))
            with e ->
                err "$" $"not valid JSON: %s{e.Message}"

        match doc with
        | Error e -> Error e
        | Ok doc ->

        use doc = doc
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            err "$" $"expected an object, got %A{root.ValueKind}"
        else

        // The schema tag is a REFUSAL, not decoration. A shim that outlives its engine — or an engine
        // that outlives its shim — must say so, rather than fold a document it does not understand.
        let schema =
            stringField "$" "schema" root
            |> Result.bind (fun s ->
                if s = LedgerSchema then
                    Ok s
                else
                    err "$.schema" $"unsupported ledger schema '%s{s}' (this engine speaks '%s{LedgerSchema}')")

        let engine = nonEmpty "$" "engine" root
        let requiredDays = countField "$" "requiredDays" root
        let minWorkers = countField "$" "minWorkers" root
        let today = prop "$" "today" root |> Result.bind (asDay "$.today")

        let reports =
            prop "$" "reports" root
            |> Result.bind (asArray "$.reports")
            |> Result.bind (fun els -> els |> List.mapi report |> collect)

        let errors =
            [ (schema |> Result.map ignore)
              (engine |> Result.map ignore)
              (requiredDays |> Result.map ignore)
              (minWorkers |> Result.map ignore)
              (today |> Result.map ignore)
              (reports |> Result.map ignore) ]
            |> List.collect (
                function
                | Error e -> e
                | Ok _ -> []
            )

        if not (List.isEmpty errors) then
            Error errors
        else

        match engine, requiredDays, minWorkers, today, reports with
        | Ok eng, Ok days, Ok workers, Ok t, Ok rs ->
            Ok
                { Engine = eng
                  RequiredDays = days
                  MinWorkers = workers
                  Today = t
                  Reports = rs }
        | _ -> Error errors

    // ================================================================================================
    // WRITING
    // ================================================================================================

    let render (verdict: Verdict<Divergence.Evidence>) : string =
        use stream = new MemoryStream()

        use w =
            new Utf8JsonWriter(stream, JsonWriterOptions(Indented = false, SkipValidation = false))

        let day (d: DateOnly) = d.ToString("yyyy-MM-dd")

        w.WriteStartObject()
        w.WriteString("schema", VerdictSchema)

        // The verdict tag is the CONTRACT. Three values, and the client translates them to three exit
        // codes — none of which is a boolean, and none of which lets "no verdict" become "green".
        let tag =
            match verdict with
            | Green _ -> "green"
            | Red _ -> "red"
            | NoVerdict _ -> "no-verdict"

        w.WriteString("verdict", tag)

        match verdict with
        | Green e ->
            w.WriteString("engine", e.Engine)

            w.WritePropertyName "window"
            w.WriteStartArray()

            for d in e.Window do
                w.WriteStringValue(day d)

            w.WriteEndArray()

            w.WritePropertyName "workers"
            w.WriteStartArray()

            for wk in e.Workers do
                w.WriteStringValue wk.Value

            w.WriteEndArray()

            w.WriteNumber("ran", e.Ran)
            w.WriteNumber("skipped", e.Skipped)
            w.WriteNumber("compared", e.Compared)
            w.WriteNumber("reason", e.ReasonDivergences)
            w.WriteNumber("discarded", e.Discarded)

        | Red reasons ->
            w.WritePropertyName "reasons"
            w.WriteStartArray()

            for r in reasons do
                w.WriteStringValue r

            w.WriteEndArray()

        | NoVerdict reason -> w.WriteString("reason", reason)

        w.WriteEndObject()
        w.Flush()

        Text.Encoding.UTF8.GetString(stream.ToArray())
