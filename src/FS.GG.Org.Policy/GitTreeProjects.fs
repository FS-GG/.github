namespace FS.GG.Org.Policy

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// Pure SHA-1 Git tree discovery for Rule (b). The caller supplies a root tree ID and raw tree
/// object bytes; authenticating that root to a repository commit and binding project blob bytes
/// to the returned IDs are separate provider obligations.
module GitTreeProjects =
    type private Entry = { Mode: string; Name: string; ObjectId: string }

    let private error path message =
        Error { Code = "git-tree-roster"; Path = path; Message = message }

    let private blobError path message =
        Error { Code = "git-blob-source"; Path = path; Message = message }

    let private canonicalId (oid: string) =
        not (isNull oid) && Regex.IsMatch(oid, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant)

    let private treeId (bytes: byte[]) =
        let prefix = Encoding.ASCII.GetBytes("tree " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\000")
        let digest: byte[] = SHA1.HashData(Array.append prefix bytes)
        Convert.ToHexString(digest).ToLowerInvariant()

    let private blobId (bytes: byte[]) =
        let prefix = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\000")
        let digest: byte[] = SHA1.HashData(Array.append prefix bytes)
        Convert.ToHexString(digest).ToLowerInvariant()

    let private projectName (path: string) =
        [ ".fsproj"; ".csproj"; ".vbproj" ]
        |> List.exists (fun extension -> path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))

    let private parseTree path (bytes: byte[]) : Result<Entry list, SyntaxDiagnostic> =
        let strictUtf8 = UTF8Encoding(false, true)
        let rec parse offset seen entries =
            if offset = bytes.Length then
                Ok(List.rev entries)
            else
                let space = Array.IndexOf(bytes, 32uy, offset)
                if space <= offset then
                    error path "malformed tree entry mode"
                else
                    let nul = Array.IndexOf(bytes, 0uy, space + 1)
                    if nul <= space + 1 || bytes.Length - nul - 1 < 20 then
                        error path "malformed tree entry name or object ID"
                    else
                        let mode = Encoding.ASCII.GetString(bytes, offset, space - offset)
                        try
                            let name = strictUtf8.GetString(bytes, space + 1, nul - space - 1)
                            if String.IsNullOrWhiteSpace name
                               || name = "." || name = ".."
                               || name.Contains('/') || name.Contains('\\') then
                                error path "malformed tree entry name"
                            elif Set.contains name seen then
                                error path "duplicate tree entry name"
                            else
                                let oidStart = nul + 1
                                let oid = Convert.ToHexString(bytes.[oidStart .. oidStart + 19]).ToLowerInvariant()
                                parse (oidStart + 20) (Set.add name seen)
                                    ({ Mode = mode; Name = name; ObjectId = oid } :: entries)
                        with :? DecoderFallbackException ->
                            error path "malformed tree entry UTF-8 name"
        parse 0 Set.empty []

    /// Enumerate every discoverable project under a complete set of raw SHA-1 Git tree objects.
    /// Returned blob IDs identify project contents but do not prove that separately supplied
    /// project bytes match them. A caller-provided root ID is not an authenticated commit anchor.
    let inspectSha1
        (rootTreeId: string)
        (treeObjects: (string * byte[]) list)
        : Result<(string * string) list, SyntaxDiagnostic> =
        if not (canonicalId rootTreeId) then
            error "<root-tree>" "root tree ID must be 40 lowercase SHA-1 hexadecimal characters"
        elif isNull (box treeObjects) || List.isEmpty treeObjects then
            error "<root-tree>" "tree object set is absent or empty"
        else
            let rec bind objects remaining =
                match remaining with
                | [] -> Ok objects
                | (oid, bytes) :: rest ->
                    if not (canonicalId oid) then
                        error "<tree-object>" "tree object ID must be 40 lowercase SHA-1 hexadecimal characters"
                    elif Map.containsKey oid objects then
                        error oid "duplicate supplied tree object ID"
                    elif isNull bytes then
                        error oid "tree object bytes are absent"
                    elif treeId bytes <> oid then
                        error oid "tree object hash does not match supplied ID"
                    else
                        bind (Map.add oid bytes objects) rest
            match bind Map.empty treeObjects with
            | Error diagnostic -> Error diagnostic
            | Ok objects ->
                let rec walk prefix ancestors oid =
                    if Set.contains oid ancestors then
                        error prefix "tree object cycle cannot certify source discovery"
                    else
                        match Map.tryFind oid objects with
                        | None -> error prefix (sprintf "referenced subtree %s is absent" oid)
                        | Some bytes ->
                            match parseTree prefix bytes with
                            | Error diagnostic -> Error diagnostic
                            | Ok entries ->
                                let rec collect found remaining =
                                    match remaining with
                                    | [] -> Ok found
                                    | entry :: rest ->
                                        let path = if prefix = "" then entry.Name else prefix + "/" + entry.Name
                                        match entry.Mode with
                                        | "40000" | "040000" ->
                                            if projectName path then
                                                error path "project-shaped tree entry is not a project blob"
                                            else
                                                match walk path (Set.add oid ancestors) entry.ObjectId with
                                                | Error diagnostic -> Error diagnostic
                                                | Ok nested -> collect (nested @ found) rest
                                        | "100644" | "100755" ->
                                            let next = if projectName path then (path, entry.ObjectId) :: found else found
                                            collect next rest
                                        | "120000" | "160000" ->
                                            error path "symlink or gitlink requires external source proof"
                                        | _ -> error path "unsupported tree entry mode"
                                collect [] entries
                match walk "" Set.empty rootTreeId with
                | Error diagnostic -> Error diagnostic
                | Ok [] -> error "<root-tree>" "rooted tree has no discoverable projects"
                | Ok roster -> Ok(List.sortBy fst roster)

    /// Bind an exact set of project bytes to the blob IDs in a verified, caller-supplied tree.
    /// The resulting SHA-256 roster is derived from those bytes for the XML graph adapter.
    /// This does not authenticate the root tree ID to a repository or commit.
    let bindSha1ProjectBlobDigests
        (rootTreeId: string)
        (treeObjects: (string * byte[]) list)
        (sources: (string * byte[]) list)
        : Result<(string * string) list, SyntaxDiagnostic> =
        match inspectSha1 rootTreeId treeObjects with
        | Error diagnostic -> Error diagnostic
        | Ok roster ->
            if isNull (box sources) || List.isEmpty sources then
                blobError "<project-blobs>" "supplied project bytes are absent or empty"
            else
                let expected = Map.ofList roster
                let rec bind seen digests remaining =
                    match remaining with
                    | [] ->
                        match roster |> List.tryFind (fun (path, _) -> not (Set.contains path seen)) with
                        | Some(path, _) -> blobError path "tree project blob is absent from supplied bytes"
                        | None -> Ok(List.sortBy fst digests)
                    | (path, bytes) :: rest ->
                        if isNull path then
                            blobError "<project-blobs>" "supplied project path is absent"
                        elif Set.contains path seen then
                            blobError path "duplicate supplied project blob"
                        else
                            match Map.tryFind path expected with
                            | None -> blobError path "supplied project blob is not in rooted tree roster"
                            | Some _ when isNull bytes -> blobError path "supplied project blob bytes are absent"
                            | Some oid ->
                                if blobId bytes <> oid then
                                    blobError path "supplied project bytes do not match tree blob ID"
                                else
                                    let digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                                    bind (Set.add path seen) ((path, digest) :: digests) rest
                bind Set.empty [] sources
