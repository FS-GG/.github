module ReleaseTrain

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Xml.Linq

let exitOk = 0
let exitFinding = 1
let exitNoVerdict = 3

let jsonOptions =
    JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

let json value = JsonSerializer.Serialize(value, jsonOptions)

let writeJson (path: string option) value =
    let rendered = json value
    match path with
    | Some target ->
        let full = Path.GetFullPath target
        Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore
        File.WriteAllText(full, rendered + Environment.NewLine)
    | None -> printfn "%s" rendered

let normalizedArgs () =
    fsi.CommandLineArgs
    |> Array.skip 1
    |> Array.filter ((<>) "--")
    |> Array.toList

type ProcessResult = {
    ExitCode: int
    StdOut: string
    StdErr: string
}

let runProcess (workingDirectory: string) (fileName: string) (arguments: string list) =
    use proc = new Process()
    proc.StartInfo <-
        ProcessStartInfo(
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        )
    arguments |> List.iter proc.StartInfo.ArgumentList.Add
    if not (proc.Start()) then failwith $"could not start {fileName}"
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()
    {
        ExitCode = proc.ExitCode
        StdOut = stdout.Result.TrimEnd()
        StdErr = stderr.Result.TrimEnd()
    }

let requireSuccess label result =
    if result.ExitCode <> 0 then
        let detail =
            [ result.StdErr; result.StdOut ]
            |> List.filter (String.IsNullOrWhiteSpace >> not)
            |> String.concat Environment.NewLine
        failwith $"{label} failed (exit {result.ExitCode}){Environment.NewLine}{detail}"
    result.StdOut

let tryRun cwd exe args =
    try
        let result = runProcess cwd exe args
        if result.ExitCode = 0 then Some result.StdOut else None
    with
    | :? ComponentModel.Win32Exception -> None

let sha256Bytes (bytes: byte array) =
    bytes |> SHA256.HashData |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()

let sha256File path = File.ReadAllBytes path |> sha256Bytes

let packagePayload (path: string) =
    use stream = File.OpenRead path
    use archive = new ZipArchive(stream, ZipArchiveMode.Read)
    archive.Entries
    |> Seq.filter (fun entry ->
        not (String.IsNullOrEmpty entry.Name)
        && not (String.Equals(entry.FullName, ".signature.p7s", StringComparison.OrdinalIgnoreCase)))
    |> Seq.map (fun entry ->
        use content = entry.Open()
        use buffer = new MemoryStream()
        content.CopyTo buffer
        entry.FullName.Replace('\\', '/'), sha256Bytes (buffer.ToArray()))
    |> Map.ofSeq

let comparePackages left right =
    let leftPayload = packagePayload left
    let rightPayload = packagePayload right
    let names =
        Set.union
            (leftPayload |> Map.keys |> Set.ofSeq)
            (rightPayload |> Map.keys |> Set.ofSeq)
    let differences =
        names
        |> Seq.choose (fun name ->
            match Map.tryFind name leftPayload, Map.tryFind name rightPayload with
            | Some leftHash, Some rightHash when leftHash = rightHash -> None
            | Some leftHash, Some rightHash -> Some $"{name}: {leftHash} != {rightHash}"
            | Some _, None -> Some $"{name}: missing from right package"
            | None, Some _ -> Some $"{name}: missing from left package"
            | None, None -> None)
        |> Seq.toList
    leftPayload.Count, rightPayload.Count, differences

let parseSimpleOption name args =
    args
    |> List.tryFindIndex ((=) name)
    |> Option.bind (fun index -> args |> List.tryItem (index + 1))

let optionValues name args =
    args
    |> List.mapi (fun index value -> index, value)
    |> List.choose (fun (index, value) ->
        if value = name then args |> List.tryItem (index + 1) else None)

let hasFlag name args = args |> List.contains name

let ensureDirectory path =
    let full = Path.GetFullPath path
    if not (Directory.Exists full) then failwith $"directory not found: {full}"
    full

let excludedDirectory (path: string) =
    let name = Path.GetFileName path
    name = ".git" || name = "bin" || name = "obj" || name = "artifacts" || name = "node_modules"

let rec filesNamed extension root =
    seq {
        for file in Directory.EnumerateFiles(root, $"*{extension}", SearchOption.TopDirectoryOnly) do
            yield file
        for directory in Directory.EnumerateDirectories root do
            if not (excludedDirectory directory) then
                yield! filesNamed extension directory
    }

let xmlValues (project: string) (elementName: string) =
    let doc = XDocument.Load project
    doc.Descendants()
    |> Seq.filter (fun element -> element.Name.LocalName = elementName)
    |> Seq.map (fun element ->
        let includeAttribute =
            element.Attributes()
            |> Seq.tryFind (fun attribute ->
                attribute.Name.LocalName = "Include" || attribute.Name.LocalName = "Update")
        match includeAttribute with
        | Some attribute -> attribute.Value
        | None -> element.Value)
    |> Seq.filter (String.IsNullOrWhiteSpace >> not)
    |> Seq.distinct
    |> Seq.toList

let normalizeNuGetBase (indexUrl: string) (client: HttpClient) =
    task {
        let! text = client.GetStringAsync indexUrl
        use doc = JsonDocument.Parse text
        let resources = doc.RootElement.GetProperty "resources"
        let mutable found = None
        for resource in resources.EnumerateArray() do
            let kind = resource.GetProperty("@type").GetString()
            if not (isNull kind) && kind.StartsWith("PackageBaseAddress", StringComparison.Ordinal) then
                found <- Some(resource.GetProperty("@id").GetString().TrimEnd('/') + "/")
        return
            found
            |> Option.defaultWith (fun () -> failwith $"no PackageBaseAddress resource in {indexUrl}")
    }

let packageUrl (baseAddress: string) (packageId: string) (version: string) =
    let id = packageId.ToLowerInvariant()
    let v = version.ToLowerInvariant()
    $"{baseAddress}{id}/{v}/{id}.{v}.nupkg"

let createHttpClient (user: string option) (token: string option) =
    let client = new HttpClient()
    client.Timeout <- TimeSpan.FromSeconds 30.0
    match user, token with
    | Some username, Some secret ->
        let raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}"))
        client.DefaultRequestHeaders.Authorization <-
            Headers.AuthenticationHeaderValue("Basic", raw)
    | _ -> ()
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FS-GG-release-train/1.0")
    client

let downloadWhenAvailable
    (client: HttpClient)
    (url: string)
    (target: string)
    (deadline: DateTimeOffset)
    (interval: TimeSpan)
    =
    task {
        let mutable complete = false
        let mutable lastStatus = HttpStatusCode.NotFound
        while not complete && DateTimeOffset.UtcNow <= deadline do
            use! response = client.GetAsync url
            lastStatus <- response.StatusCode
            if response.IsSuccessStatusCode then
                let! bytes = response.Content.ReadAsByteArrayAsync()
                File.WriteAllBytes(target, bytes)
                complete <- true
            elif response.StatusCode = HttpStatusCode.NotFound then
                do! System.Threading.Tasks.Task.Delay interval
            else
                let! detail = response.Content.ReadAsStringAsync()
                failwith $"{url} returned HTTP {int response.StatusCode}: {detail}"
        if not complete then
            failwith $"{url} did not resolve before timeout; last HTTP status was {int lastStatus}"
    }

let repoRows (registryPath: string) =
    let row =
        Regex(
            @"^\s*-\s*\{\s*id:\s*([^,\s]+),\s*full:\s*([^,\s]+),",
            RegexOptions.Compiled
        )
    File.ReadLines registryPath
    |> Seq.choose (fun line ->
        let m = row.Match line
        if m.Success then Some(m.Groups[1].Value, m.Groups[2].Value) else None)
    |> Seq.toList

let selfTestPackageComparison () =
    let root = Path.Combine(Path.GetTempPath(), $"release-train-selftest-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore
    let makeZip (path: string) (signature: string option) (payload: string) =
        use stream = File.Create path
        use archive = new ZipArchive(stream, ZipArchiveMode.Create)
        let write (name: string) (content: string) =
            let entry = archive.CreateEntry name
            use writer = new StreamWriter(entry.Open())
            writer.Write content
        write "content/file.txt" payload
        signature |> Option.iter (write ".signature.p7s")
    let left = Path.Combine(root, "left.nupkg")
    let signed = Path.Combine(root, "signed.nupkg")
    let changed = Path.Combine(root, "changed.nupkg")
    makeZip left None "same"
    makeZip signed (Some "signature") "same"
    makeZip changed None "different"
    let leftCount, signedCount, sameDiff = comparePackages left signed
    let _, _, changedDiff = comparePackages left changed
    if leftCount <> 1 || signedCount <> 1 || not sameDiff.IsEmpty || changedDiff.IsEmpty then
        failwith "package payload comparison self-test failed"
    Directory.Delete(root, true)
