namespace FS.GG.Coord

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

module RootIdentityPolicy =
    type Policy = { RootLogins: Set<string>; Sha256: string }

    type private ResourceAnchor = ResourceAnchor

    let private validLogin (login: string) =
        let alphanumeric c = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
        login.Length > 0 && login.Length <= 39
        && alphanumeric login[0] && alphanumeric login[login.Length - 1]
        && (login |> Seq.forall (fun c -> alphanumeric c || c = '-'))

    // Internal parser is visible to focused regression tests, never an authorization input.
    let internal parse (raw: byte array) : Result<Policy, string> =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(raw))
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error "Root identity policy must be an object"
            else
                let properties = root.EnumerateObject() |> Seq.toList
                let names = properties |> List.map (fun p -> p.Name)
                if names.Length <> 2 || Set.ofList names <> set [ "schema"; "rootLogins" ] then
                    Error "Unknown or duplicate Root identity policy properties"
                elif root.GetProperty("schema").ValueKind <> JsonValueKind.String
                     || root.GetProperty("schema").GetString() <> "fsgg.root-identity-policy/v1" then
                    Error "Unknown Root identity policy schema"
                elif root.GetProperty("rootLogins").ValueKind <> JsonValueKind.Array then
                    Error "Root identity policy principals must be an array"
                else
                    let entries = root.GetProperty("rootLogins").EnumerateArray() |> Seq.toList
                    if entries.IsEmpty || entries |> List.exists (fun e -> e.ValueKind <> JsonValueKind.String) then
                        Error "Invalid Root identity policy principals"
                    else
                        let logins = entries |> List.map (fun e -> e.GetString())
                        if logins |> List.exists (validLogin >> not)
                           || (logins |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList |> Set.count) <> logins.Length
                           || logins <> (logins |> List.sortWith (fun a b -> StringComparer.Ordinal.Compare(a, b))) then
                            Error "Invalid or duplicate Root identity policy principals"
                        else
                            // Login grammar is ASCII and needs no JSON escaping. Exact bytes reject BOM,
                            // whitespace, alternative escaping, trailing data and duplicate properties.
                            let quoted = logins |> List.map (fun s -> "\"" + s + "\"") |> String.concat ","
                            let canonical = "{\"rootLogins\":[" + quoted + "],\"schema\":\"fsgg.root-identity-policy/v1\"}\n"
                            if not (raw.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(canonical).AsSpan())) then
                                Error "Noncanonical Root identity policy"
                            else
                                Ok { RootLogins = Set.ofList logins; Sha256 = Convert.ToHexString(SHA256.HashData raw).ToLowerInvariant() }
        with
        | :? JsonException -> Error "Malformed Root identity policy"
        | :? ArgumentException -> Error "Malformed Root identity policy"

    let policy : Result<Policy, string> =
        try
            use stream = typeof<ResourceAnchor>.Assembly.GetManifestResourceStream("FS.GG.Coord.Core.RootIdentityPolicy.json")
            if isNull stream then Error "Root identity policy resource unavailable"
            else
                use bytes = new MemoryStream()
                stream.CopyTo bytes
                parse (bytes.ToArray())
        with
        | :? IOException -> Error "Root identity policy resource unavailable"
        | :? UnauthorizedAccessException -> Error "Root identity policy resource unavailable"

    let rootLogins = policy |> Result.map (fun p -> p.RootLogins)
    let digest = policy |> Result.map (fun p -> p.Sha256)

    let isRoot (login: string) =
        policy |> Result.map (fun p -> p.RootLogins |> Seq.exists (fun root -> String.Equals(root, login, StringComparison.OrdinalIgnoreCase)))
