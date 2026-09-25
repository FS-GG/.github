namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.RegularExpressions

/// Exact, read-only GitHub GraphQL commit-membership response contract. It validates returned
/// facts but supplies no credentials, network transport, accepted pin source, or authority.
module GitHubCommitMembership =
    type ExactRequest =
        { Document: string
          Owner: string
          Name: string
          CommitId: string }

    type IReadOnlyGraphQlReader =
        abstract ExecuteExact: ExactRequest -> Result<byte[], unit>

    type ProvisionalMembership =
        { RepositoryNodeId: string
          CommitId: string
          TreeId: string }

    let private document =
        "query CommitMembership($owner: String!, $name: String!, $commit: GitObjectID!) { "
        + "repository(owner: $owner, name: $name, followRenames: false) { "
        + "id nameWithOwner object(oid: $commit) { __typename ... on Commit { oid tree { oid } } } } }"

    let private error path message =
        Error { Code = "github-commit-membership"; Path = path; Message = message }

    let private canonicalSha1 (value: string) =
        not (isNull value) && Regex.IsMatch(value, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)

    /// The concrete HTTP reader must not execute arbitrary GraphQL documents through this port.
    let internal isExactReadRequest (request: ExactRequest) =
        not (isNull (box request))
        && String.Equals(request.Document, document, StringComparison.Ordinal)
        && not (isNull request.Owner)
        && Regex.IsMatch(request.Owner, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)
        && not (isNull request.Name)
        && Regex.IsMatch(request.Name, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)
        && canonicalSha1 request.CommitId

    let rec private duplicateKey (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            let names = HashSet<string>(StringComparer.Ordinal)
            element.EnumerateObject()
            |> Seq.exists (fun property -> not (names.Add property.Name) || duplicateKey property.Value)
        | JsonValueKind.Array -> element.EnumerateArray() |> Seq.exists duplicateKey
        | _ -> false

    let private property name (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then None
        else
            element.EnumerateObject()
            |> Seq.tryFind (fun property -> String.Equals(property.Name, name, StringComparison.Ordinal))
            |> Option.map (fun property -> property.Value)

    let private requiredObject path name parent =
        match property name parent with
        | Some value when value.ValueKind = JsonValueKind.Object -> Ok value
        | _ -> error path (sprintf "%s object is missing or null" name)

    let private requiredString path name parent =
        match property name parent with
        | Some value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | _ -> error path (sprintf "%s string is missing or null" name)

    let private inspectResponse (pin: GitCommitProvenance.ExactCommitPin) expectedTreeId (bytes: byte[]) =
        try
            use parsed = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes))
            let root = parsed.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                error "<graphql>" "GraphQL response must be an object"
            elif duplicateKey root then
                error "<graphql>" "duplicate JSON key in GraphQL response"
            elif property "errors" root |> Option.isSome then
                error "<graphql>" "GraphQL errors prevent complete commit membership proof"
            else
                match requiredObject "<graphql>" "data" root with
                | Error diagnostic -> Error diagnostic
                | Ok data ->
                    match requiredObject "<graphql>" "repository" data with
                    | Error diagnostic -> Error diagnostic
                    | Ok repository ->
                        match requiredString "<repository>" "id" repository,
                              requiredString "<repository>" "nameWithOwner" repository with
                        | Ok nodeId, Ok fullName ->
                            if not (String.Equals(nodeId, pin.RepositoryNodeId, StringComparison.Ordinal))
                               || not (String.Equals(fullName, pin.RepositoryFullName, StringComparison.Ordinal)) then
                                error "<repository>" "GraphQL repository identity differs from exact pin"
                            else
                                match requiredObject "<repository>" "object" repository with
                                | Error diagnostic -> Error diagnostic
                                | Ok gitObject ->
                                    match requiredString "<object>" "__typename" gitObject,
                                          requiredString "<object>" "oid" gitObject with
                                    | Ok "Commit", Ok oid ->
                                        if not (String.Equals(oid, pin.CommitId, StringComparison.Ordinal)) then
                                            error "<object>" "GraphQL commit ID differs from exact pin"
                                        else
                                            match requiredObject "<object>" "tree" gitObject with
                                            | Error diagnostic -> Error diagnostic
                                            | Ok tree ->
                                                match requiredString "<tree>" "oid" tree with
                                                | Error diagnostic -> Error diagnostic
                                                | Ok treeId when not (String.Equals(treeId, expectedTreeId, StringComparison.Ordinal)) ->
                                                    error "<tree>" "GraphQL tree ID differs from supplied root tree ID"
                                                | Ok treeId ->
                                                    Ok { RepositoryNodeId = nodeId; CommitId = oid; TreeId = treeId }
                                    | Ok _, _ -> error "<object>" "GraphQL object is not a Commit"
                                    | Error diagnostic, _ | _, Error diagnostic -> Error diagnostic
                        | Error diagnostic, _ | _, Error diagnostic -> Error diagnostic
        with :? JsonException ->
            error "<graphql>" "GraphQL response JSON is malformed"

    /// Query the exact repository and commit OID, then validate all selected fields. The reader
    /// must later be implemented with authenticated GitHub transport and a complete HTTP 200
    /// response; fixture or caller-supplied bytes alone cannot establish repository membership.
    let inspectProvisionalMembership
        (pin: GitCommitProvenance.ExactCommitPin)
        (expectedTreeId: string)
        (reader: IReadOnlyGraphQlReader)
        : Result<ProvisionalMembership, SyntaxDiagnostic> =
        if isNull (box pin)
           || String.IsNullOrWhiteSpace pin.RepositoryNodeId
           || isNull pin.RepositoryFullName
           || not (Regex.IsMatch(pin.RepositoryFullName, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)) then
            error "<pin>" "exact repository identity is absent or malformed"
        elif not (canonicalSha1 pin.CommitId) || not (canonicalSha1 expectedTreeId) then
            error "<pin>" "commit and tree IDs must be exact lowercase SHA-1 values"
        elif isNull (box reader) then
            error "<graphql>" "read-only GraphQL reader is unavailable"
        else
            let names = pin.RepositoryFullName.Split('/')
            let request =
                { Document = document; Owner = names.[0]; Name = names.[1]; CommitId = pin.CommitId }
            let response =
                try reader.ExecuteExact request
                with _ -> Error ()
            match response with
            | Error () -> error "<graphql>" "read-only GraphQL response is unavailable"
            | Ok bytes when isNull bytes || Array.isEmpty bytes ->
                error "<graphql>" "read-only GraphQL response is absent"
            | Ok bytes when bytes.Length > 65536 ->
                error "<graphql>" "read-only GraphQL response exceeds bounded size"
            | Ok bytes -> inspectResponse pin expectedTreeId (Array.copy bytes)
