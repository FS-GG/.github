namespace FS.GG.Telemetry.Host

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord

module NativeSourceVerification =
    type Limits =
        {
            Timeout: TimeSpan
            StdoutBytes: int
            StderrBytes: int
        }

    let private productionLimits =
        { Timeout = TimeSpan.FromSeconds 30.; StdoutBytes = 1024 * 1024; StderrBytes = 64 * 1024 }

    let private unavailable () = Error [ "native source verification unavailable" ]

    let private sha256 (bytes: byte array) =
        Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

    let private exactNames (value: JsonObject) (expected: Set<string>) =
        let names = value |> Seq.map _.Key |> Seq.toArray
        names.Length = expected.Count
        && Array.distinct names |> Array.length = names.Length
        && Set.ofArray names = expected

    let rec private noDuplicateProperties (value: JsonElement) =
        match value.ValueKind with
        | JsonValueKind.Object ->
            let properties = value.EnumerateObject() |> Seq.toArray
            properties.Length = (properties |> Array.map _.Name |> Array.distinct |> Array.length)
            && (properties |> Array.forall (fun property -> noDuplicateProperties property.Value))
        | JsonValueKind.Array -> value.EnumerateArray() |> Seq.forall noDuplicateProperties
        | _ -> true

    let private immutableFile executableRequired path expectedLength expectedDigest =
        let info = FileInfo path
        let rec realAncestors (directory: DirectoryInfo) =
            isNull directory || (directory.Exists && isNull directory.LinkTarget && realAncestors directory.Parent)
        info.Exists
        && Path.IsPathFullyQualified path
        && Path.GetFullPath(path) = path
        && isNull info.LinkTarget
        && realAncestors info.Directory
        && info.Length = expectedLength
        && (File.GetUnixFileMode path &&& (UnixFileMode.GroupWrite ||| UnixFileMode.OtherWrite)) = enum 0
        && (not executableRequired
            || (File.GetUnixFileMode path &&&
                (UnixFileMode.UserExecute ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherExecute)) <> enum 0)
        && sha256 (File.ReadAllBytes path) = expectedDigest

    let private validateManifest (verifier: NativeVerifierConfig) =
        try
            let bytes = File.ReadAllBytes verifier.RuntimeManifestPath
            if verifier.ModuleSha256 <> "8d6a33beae9a4de84fa7a703809e9b1a1656359a085f92091cf56de3b77fd3ba"
               || bytes.Length = 0 || bytes.Length > 1024 * 1024 || sha256 bytes <> verifier.RuntimeManifestSha256 then false
            else
                use document = JsonDocument.Parse bytes
                if not (noDuplicateProperties document.RootElement) then false
                else
                    let manifest = JsonNode.Parse(bytes).AsObject()
                    let fields = set [ "schema"; "sourceRevision"; "runtimeImageDigest"; "runtimeExecutablePath"; "modulePath"; "files" ]
                    if not (exactNames manifest fields)
                       || manifest["schema"].GetValue<string>() <> "fsgg.telemetry.native-verifier-runtime/1"
                       || not (Regex.IsMatch(manifest["sourceRevision"].GetValue<string>(), "^[0-9a-f]{40}$"))
                       || not (Regex.IsMatch(manifest["runtimeImageDigest"].GetValue<string>(), "^sha256:[0-9a-f]{64}$"))
                       || manifest["runtimeExecutablePath"].GetValue<string>() <> verifier.RuntimeExecutablePath
                       || manifest["modulePath"].GetValue<string>() <> verifier.ModulePath then false
                    else
                        let files = manifest["files"].AsArray()
                        if files.Count = 0 || files.Count > 4096 then false
                        else
                            let mutable total = 0L
                            let mutable previous: string = null
                            let mutable valid = true
                            for entryNode in files do
                                let entry = entryNode.AsObject()
                                let entryFields = set [ "path"; "bytes"; "sha256" ]
                                if not (exactNames entry entryFields) then valid <- false
                                else
                                    let path = entry["path"].GetValue<string>()
                                    let length = entry["bytes"].GetValue<int64>()
                                    let digest = entry["sha256"].GetValue<string>()
                                    if isNull previous |> not && String.CompareOrdinal(previous, path) >= 0 then valid <- false
                                    previous <- path
                                    if length <= 0L || length > 512L * 1024L * 1024L
                                       || not (Regex.IsMatch(digest, "^[0-9a-f]{64}$"))
                                       || not (immutableFile (path = verifier.RuntimeExecutablePath) path length digest) then
                                        valid <- false
                                    total <- total + length
                            let declared =
                                files
                                |> Seq.map (fun item -> item["path"].GetValue<string>())
                                |> Set.ofSeq
                            valid && total <= 512L * 1024L * 1024L
                            && declared.Contains verifier.RuntimeExecutablePath
                            && declared.Contains verifier.ModulePath
                            && immutableFile true verifier.RuntimeExecutablePath
                                (FileInfo(verifier.RuntimeExecutablePath).Length) verifier.RuntimeExecutableSha256
                            && immutableFile false verifier.ModulePath
                                (FileInfo(verifier.ModulePath).Length) verifier.ModuleSha256
        with _ -> false

    let private readBounded (stream: Stream) maximum (cancellation: CancellationTokenSource) =
        task {
            use output = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 8192
            let mutable complete = false
            while not complete do
                let! count = stream.ReadAsync(buffer.AsMemory(), cancellation.Token)
                if count = 0 then complete <- true
                elif output.Length + int64 count > int64 maximum then
                    cancellation.Cancel()
                    raise (InvalidDataException "native verifier output exceeded its bound")
                else output.Write(buffer, 0, count)
            return output.ToArray()
        }

    let private writePrivate directory name (bytes: byte array) =
        let path = Path.Combine(directory, name)
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write,
                                        Share = FileShare.None, Options = FileOptions.WriteThrough,
                                        UnixCreateMode = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
        use stream = new FileStream(path, options)
        stream.Write(bytes)
        stream.Flush true
        path

    let private runVerifier limits (verifier: NativeVerifierConfig) evidenceRoot
                            (captureBytes: byte array) (snapshotBytes: byte array) : byte array =
        let directory = Path.Combine(evidenceRoot, ".native-source-verification-" + Guid.NewGuid().ToString("N"))
        try
            Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
            let capture = writePrivate directory "capture.json" captureBytes
            let snapshot = writePrivate directory "snapshot.json" snapshotBytes
            let start = ProcessStartInfo()
            start.FileName <- verifier.RuntimeExecutablePath
            start.ArgumentList.Add "-I"
            start.ArgumentList.Add "-S"
            start.ArgumentList.Add "-B"
            start.ArgumentList.Add verifier.ModulePath
            start.ArgumentList.Add "verify"
            start.ArgumentList.Add "--capture"
            start.ArgumentList.Add capture
            start.ArgumentList.Add "--telemetry-snapshot"
            start.ArgumentList.Add snapshot
            start.UseShellExecute <- false
            start.RedirectStandardInput <- true
            start.RedirectStandardOutput <- true
            start.RedirectStandardError <- true
            start.CreateNoWindow <- true
            start.WorkingDirectory <- directory
            start.Environment.Clear()
            use child = new Process(StartInfo = start)
            if not (child.Start()) then invalidOp "native verifier failed to start"
            child.StandardInput.Close()
            use cancellation = new CancellationTokenSource(limits.Timeout)
            let stdout = readBounded child.StandardOutput.BaseStream limits.StdoutBytes cancellation
            let stderr = readBounded child.StandardError.BaseStream limits.StderrBytes cancellation
            let exited = child.WaitForExitAsync cancellation.Token
            try
                Task.WhenAll([| stdout :> Task; stderr :> Task; exited |]).GetAwaiter().GetResult()
            with _ ->
                if not child.HasExited then child.Kill true
                try child.WaitForExit()
                with _ -> ()
                reraise()
            let output = stdout.GetAwaiter().GetResult()
            stderr.GetAwaiter().GetResult() |> ignore
            if child.ExitCode <> 0 || output.Length = 0 then invalidOp "native verifier refused"
            output
        finally
            if Directory.Exists directory then Directory.Delete(directory, true)

    let verifyRetainedWithLimits limits verifier evidenceRoot (captureBytes: byte array) (snapshotBytes: byte array)
                                 (retainedVerificationBytes: byte array) =
        try
            if limits.Timeout <= TimeSpan.Zero || limits.StdoutBytes <= 0 || limits.StderrBytes <= 0
               || not (validateManifest verifier) then unavailable ()
            else
                use retainedDocument = JsonDocument.Parse retainedVerificationBytes
                if not (noDuplicateProperties retainedDocument.RootElement) then unavailable ()
                else
                    let actual = runVerifier limits verifier evidenceRoot captureBytes snapshotBytes
                    use actualDocument = JsonDocument.Parse actual
                    if not (noDuplicateProperties actualDocument.RootElement) then unavailable ()
                    else
                        let expectedCanonical = CanonicalJson.canonicalize retainedVerificationBytes
                        let actualCanonical = CanonicalJson.canonicalize actual
                        match expectedCanonical, actualCanonical with
                        | Ok expected, Ok observed when expected = observed ->
                            let verification = JsonNode.Parse(actual).AsObject()
                            let fields =
                                set [ "schema"; "status"; "outcome"; "captureDigest"; "missingDescendants";
                                      "foreignDescendants"; "mismatchedThreads"; "missingTurns"; "foreignTurns";
                                      "missingUsage"; "foreignUsage"; "mismatchedUsage" ]
                            let gaps =
                                [ "missingDescendants"; "foreignDescendants"; "mismatchedThreads"; "missingTurns";
                                  "foreignTurns"; "missingUsage"; "foreignUsage"; "mismatchedUsage" ]
                            if exactNames verification fields
                               && verification["schema"].GetValue<string>() = "fsgg.learn.native-source-verification/1"
                               && verification["status"].GetValue<string>() = "verified"
                               && verification["outcome"].GetValue<string>() = "native-census-and-usage-reconciled-at-capture"
                               && gaps |> List.forall (fun name -> verification[name].AsArray().Count = 0)
                               && validateManifest verifier then Ok()
                            else unavailable ()
                        | _ -> unavailable ()
        with _ -> unavailable ()

    let verifyRetained verifier evidenceRoot captureBytes snapshotBytes retainedVerificationBytes =
        verifyRetainedWithLimits productionLimits verifier evidenceRoot captureBytes snapshotBytes retainedVerificationBytes
