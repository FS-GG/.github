namespace FS.GG.Org.Policy

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// Pure SHA-1 Git tree discovery for Rule (b), over supplied bytes or an exact-object reader.
/// Authenticating the root to a repository commit remains a separate provider obligation.
module GitTreeProjects =
    type private Entry = { Mode: string; Name: string; ObjectId: string }

    type ObjectKind = Tree | Blob

    type ObjectObservation =
        { ObjectId: string
          Kind: ObjectKind
          Bytes: byte[] }

    type IReadOnlyObjectReader =
        abstract ReadExact: ObjectKind * string -> Result<ObjectObservation, unit>

    let private error path message =
        Error { Code = "git-tree-roster"; Path = path; Message = message }

    let private blobError path message =
        Error { Code = "git-blob-source"; Path = path; Message = message }

    let private providerError path message =
        Error { Code = "git-object-provider"; Path = path; Message = message }

    let private canonicalId (oid: string) =
        not (isNull oid) && Regex.IsMatch(oid, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)

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

    let private compareTreeEntries (left: Entry) (right: Entry) =
        // Git compares raw name bytes, treating a directory's terminal byte as '/'.
        let leftName = Encoding.UTF8.GetBytes(left.Name)
        let rightName = Encoding.UTF8.GetBytes(right.Name)
        let terminal entry = if entry.Mode = "40000" then int '/' else 0
        let rec compareAt index =
            let leftByte = if index < leftName.Length then int leftName.[index] else terminal left
            let rightByte = if index < rightName.Length then int rightName.[index] else terminal right
            if leftByte <> rightByte then compare leftByte rightByte
            elif index >= leftName.Length && index >= rightName.Length then 0
            else compareAt (index + 1)
        compareAt 0

    let private ignoredDotgitAliasChar c =
        c = '\u200c' || c = '\u200d' || c = '\u200e' || c = '\u200f'
        || (c >= '\u202a' && c <= '\u202e')
        || (c >= '\u206a' && c <= '\u206f')
        || c = '\ufeff'

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
                            // These Git-ignored format characters can conceal a .git tree name.
                            let aliasName =
                                String(name.ToCharArray() |> Array.filter (ignoredDotgitAliasChar >> not))
                            let normalizedName = aliasName.TrimEnd([| ' '; '.' |])
                            let streamSeparator = aliasName.IndexOf(':')
                            let streamBase =
                                if streamSeparator < 0 then normalizedName
                                else aliasName.Substring(0, streamSeparator).TrimEnd([| ' '; '.' |])
                            if mode.StartsWith("0", StringComparison.Ordinal) then
                                error path "zero-padded tree entry mode"
                            elif String.IsNullOrWhiteSpace name
                               || name = "." || name = ".."
                               || name.Contains('/') || name.Contains('\\') then
                                error path "malformed tree entry name"
                            elif String.Equals(normalizedName, ".git", StringComparison.OrdinalIgnoreCase)
                                 || (streamSeparator >= 0
                                     && String.Equals(streamBase, ".git", StringComparison.OrdinalIgnoreCase))
                                 || String.Equals(normalizedName, "git~1", StringComparison.OrdinalIgnoreCase) then
                                error path "reserved .git tree entry name"
                            elif Set.contains name seen then
                                error path "duplicate tree entry name"
                            else
                                let oidStart = nul + 1
                                let oid = Convert.ToHexString(bytes.[oidStart .. oidStart + 19]).ToLowerInvariant()
                                let entry = { Mode = mode; Name = name; ObjectId = oid }
                                match entries with
                                | previous :: _ when compareTreeEntries previous entry >= 0 ->
                                    error path "noncanonical tree entry order"
                                | _ -> parse (oidStart + 20) (Set.add name seen) (entry :: entries)
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
                                        | "40000" ->
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

    /// Fetch the reachable tree closure and every discovered project blob by exact SHA-1 ID.
    /// Each read's type, returned ID and raw bytes are checked before any graph can be formed.
    /// This port has no installed local/network reader; root commit provenance is separate.
    let materializeReadOnlySha1Snapshot
        (rootTreeId: string)
        (reader: IReadOnlyObjectReader)
        : Result<(string * byte[]) list * (string * byte[]) list, SyntaxDiagnostic> =
        if not (canonicalId rootTreeId) then
            providerError "<root-tree>" "root tree ID must be exact lowercase SHA-1"
        elif isNull (box reader) then
            providerError "<git-objects>" "read-only object reader is unavailable"
        else
            let readVerified kind path oid =
                let observed =
                    try reader.ReadExact(kind, oid)
                    with _ -> Error ()
                match observed with
                | Error () -> providerError path (sprintf "requested %A object %s is absent or unavailable" kind oid)
                | Ok value when isNull (box value) -> providerError path "object observation is absent"
                | Ok value when value.Kind <> kind -> providerError path "returned Git object kind differs from request"
                | Ok value when not (String.Equals(value.ObjectId, oid, StringComparison.Ordinal)) ->
                    providerError path "returned Git object ID differs from request"
                | Ok value when isNull value.Bytes -> providerError path "returned Git object bytes are absent"
                | Ok value ->
                    let bytes = Array.copy value.Bytes
                    let actual = if kind = Tree then treeId bytes else blobId bytes
                    if actual <> oid then providerError path "returned Git object hash differs from requested ID"
                    else Ok bytes

            let rec gatherTree path ancestors objects oid =
                if Set.contains oid ancestors then
                    providerError path "tree object cycle cannot certify closure"
                elif Map.containsKey oid objects then
                    Ok objects
                else
                    match readVerified Tree path oid with
                    | Error diagnostic -> Error diagnostic
                    | Ok bytes ->
                        match parseTree path bytes with
                        | Error diagnostic -> Error diagnostic
                        | Ok entries ->
                            let rec gatherChildren found remaining =
                                match remaining with
                                | [] -> Ok found
                                | entry :: rest ->
                                    let entryPath = if path = "" then entry.Name else path + "/" + entry.Name
                                    match entry.Mode with
                                    | "40000" | "040000" ->
                                        match gatherTree entryPath (Set.add oid ancestors) found entry.ObjectId with
                                        | Error diagnostic -> Error diagnostic
                                        | Ok next -> gatherChildren next rest
                                    | _ -> gatherChildren found rest
                            gatherChildren (Map.add oid bytes objects) entries

            match gatherTree "" Set.empty Map.empty rootTreeId with
            | Error diagnostic -> Error diagnostic
            | Ok objects ->
                let trees = Map.toList objects
                match inspectSha1 rootTreeId trees with
                | Error diagnostic -> Error diagnostic
                | Ok roster ->
                    let rec gatherBlobs sources remaining =
                        match remaining with
                        | [] -> Ok(trees, List.rev sources)
                        | (path, oid) :: rest ->
                            match readVerified Blob path oid with
                            | Error diagnostic -> Error diagnostic
                            | Ok bytes -> gatherBlobs ((path, bytes) :: sources) rest
                    gatherBlobs [] roster
