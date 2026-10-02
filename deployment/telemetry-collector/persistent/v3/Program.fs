open System
open System.IO
open FSGG.Telemetry.PersistentV3

let private privateParent (path: string) =
    if not (Path.IsPathFullyQualified path) || Path.GetFullPath(path) <> path then false
    else
        let parent = DirectoryInfo(Path.GetDirectoryName path)
        parent.Exists
        && isNull parent.LinkTarget
        && (OperatingSystem.IsWindows()
            || File.GetUnixFileMode(parent.FullName) =
               (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute))

let private writeNewPrivate path (bytes: byte array) =
    let options =
        FileStreamOptions(
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
            UnixCreateMode = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
    use stream = new FileStream(path, options)
    stream.Write(bytes, 0, bytes.Length)
    stream.Flush(true)

[<EntryPoint>]
let main argv =
    match argv with
    | [| "prepare"; input; output |] ->
        try
            if not (privateParent output) then
                eprintfn "persistent-v3-preparation-refused: output parent custody"
                2
            else
                match Preparation.prepare (File.ReadAllBytes input) with
                | Error message -> eprintfn "%s" message; 2
                | Ok bytes ->
                    writeNewPrivate output bytes
                    0
        with ex -> eprintfn "persistent-v3-preparation-refused: %s" (ex.Message); 2
    | _ -> eprintfn "usage: PersistentV3.Preparation prepare <input.json> <output.json>"; 2
