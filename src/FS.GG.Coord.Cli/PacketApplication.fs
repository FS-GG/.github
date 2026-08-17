namespace FS.GG.Coord.Cli

open System.IO
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.Cli.Options

module PacketApplication =
    let private refusal message =
        printfn
            "{\"schema\":\"%s\",\"kind\":\"refusal\",\"reason\":%s}"
            FindingPacket.ResultSchema
            (JsonSerializer.Serialize(message: string))

        ExitCode.toInt ExitCode.Error

    let private report (findings: FindingPacket.Finding list) =
        // One line per offending FIELD, on stderr, because the reader is a finder deciding what to fix
        // — not a machine. The single-line refusal document on stdout stays parseable.
        for finding in findings do
            eprintfn "  %s %s" finding.Field finding.Detail

        refusal (
            findings
            |> List.map (fun finding -> $"%s{finding.Field} %s{finding.Detail}")
            |> String.concat "; "
        )

    let run (opts: Options) =
        match opts.Args with
        | [ "validate"; path ] ->
            let document =
                try
                    Ok(File.ReadAllText path)
                with :? IOException as ex ->
                    Error $"cannot read packet: {ex.Message}"

            match document with
            | Error reason -> refusal reason
            | Ok document ->
                match FindingPacket.parse document with
                | Error findings -> report findings
                | Ok packet ->
                    match FindingPacket.validate packet with
                    | Error findings -> report findings
                    | Ok packet ->
                        printfn "%s" (FindingPacket.renderResult packet)
                        ExitCode.toInt ExitCode.Green
        | "validate" :: _ -> refusal "usage: packet validate <packet.json>"
        | other :: _ -> refusal $"unknown packet action '{other}' (expected validate)"
        | [] -> refusal "usage: packet validate <packet.json>"
