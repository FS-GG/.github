module HistoricalLossCapture.Program

open System
open System.Globalization
open System.IO
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.HistoricalLossRetainedNativeCensus

let private usage =
    "usage: historical-loss-capture capture --horizon <UTC round-trip instant> --output <new absolute path>"

let private privateDirectoryMode =
    UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute

let private parse (args: string array) =
    if args.Length <> 5 || args.[0] <> "capture" || args.[1] <> "--horizon" || args.[3] <> "--output" then
        Error "arguments"
    else
        let mutable parsed = DateTimeOffset.MinValue
        let horizon = args.[2]
        let output = args.[4]
        if not (DateTimeOffset.TryParseExact(horizon, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, &parsed))
           || parsed.Offset <> TimeSpan.Zero || parsed > DateTimeOffset.UtcNow then
            Error "horizon"
        elif not (Path.IsPathFullyQualified output) || String.IsNullOrWhiteSpace(Path.GetFileName output) then
            Error "output-path"
        else
            Ok(horizon, output)

let private privateDirectory output =
    try
        let directory = Path.GetDirectoryName(Path.GetFullPath output)
        let info = DirectoryInfo directory
        info.Exists && isNull info.LinkTarget && File.GetUnixFileMode(directory) = privateDirectoryMode
    with _ -> false

[<EntryPoint>]
let main args =
    match parse args with
    | Error reason ->
        Console.Error.WriteLine($"historical-loss-capture: refused {reason}; {usage}")
        2
    | Ok(horizon, output) when not (OperatingSystem.IsLinux()) || not (privateDirectory output) ->
        Console.Error.WriteLine "historical-loss-capture: refused private-output-directory"
        2
    | Ok(horizon, output) ->
        let token = Environment.GetEnvironmentVariable "FSGG_HISTORICAL_CAPTURE_TOKEN"
        if String.IsNullOrWhiteSpace token then
            Console.Error.WriteLine "historical-loss-capture: refused missing-token"
            2
        elif File.Exists output || Directory.Exists output then
            Console.Error.WriteLine "historical-loss-capture: refused existing-output"
            2
        else
            let fixtureBase = Environment.GetEnvironmentVariable "FSGG_HISTORICAL_CAPTURE_FIXTURE_API_BASE"
            let apiBase =
                if String.IsNullOrWhiteSpace fixtureBase then "https://api.github.com"
                else fixtureBase
            let fixtureAllowed =
                if String.IsNullOrWhiteSpace fixtureBase then true
                else
                    match Uri.TryCreate(apiBase, UriKind.Absolute) with
                    | true, uri ->
                        (uri.Host = "127.0.0.1" || uri.Host = "localhost")
                        && (uri.Scheme = "http" || uri.Scheme = "https")
                        && uri.UserInfo = "" && uri.AbsolutePath = "/" && uri.Query = "" && uri.Fragment = ""
                    | _ -> false
            if not fixtureAllowed then
                Console.Error.WriteLine "historical-loss-capture: refused fixture-api-base"
                2
            else
                try
                    use transport = new HttpTransport(apiBase, token)
                    match collectTwoPassPrivate (transport :> IVersionedSinglePageGitHubTransport) apiBase horizon output with
                    | Error _ ->
                        Console.Error.WriteLine "historical-loss-capture: refused native-census"
                        1
                    | Ok _ ->
                        Console.WriteLine "historical-loss-capture: saved private two-pass evidence"
                        0
                with _ ->
                    Console.Error.WriteLine "historical-loss-capture: refused capture-runtime"
                    1
