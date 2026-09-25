namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.RegularExpressions

/// Provisional pin derived from an exact protected-main branch observation. The REST reader is
/// deliberately a port: an authenticated, no-redirect implementation and accepted repository
/// identity still have to be installed before this can establish policy source authority.
module GitHubProtectedBranchPin =
    type ExactRepository =
        { RepositoryNodeId: string
          RepositoryFullName: string }

    type ExactRequest =
        { Url: string
          Owner: string
          Name: string
          Branch: string }

    type BranchResponse =
        { StatusCode: int
          ResponseUrl: string
          MediaType: string
          Body: byte[] }

    type IReadOnlyProtectedBranchReader =
        abstract ReadExact: ExactRequest -> Result<BranchResponse, unit>

    let private error path message =
        Error { Code = "github-protected-pin"; Path = path; Message = message }

    let private validSegment (value: string) =
        not (isNull value)
        && value <> "." && value <> ".."
        && Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)

    /// The concrete transport must reject arbitrary URLs before attaching its bearer token.
    let internal isExactReadRequest (request: ExactRequest) =
        not (isNull (box request))
        && validSegment request.Owner
        && validSegment request.Name
        && String.Equals(request.Branch, "main", StringComparison.Ordinal)
        && String.Equals(request.Url,
                         sprintf "https://api.github.com/repos/%s/%s/branches/main" request.Owner request.Name,
                         StringComparison.Ordinal)

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

    let private textField path name parent =
        match property name parent with
        | Some value when value.ValueKind = JsonValueKind.String -> Ok(value.GetString())
        | _ -> error path (sprintf "%s field is absent or null" name)

    let private parsePin (repository: ExactRepository) (bytes: byte[])
        : Result<GitCommitProvenance.ExactCommitPin, SyntaxDiagnostic> =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes))
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                error "<branch>" "branch response is not an object"
            elif duplicateKey root then
                error "<branch>" "duplicate JSON key in branch response"
            else
                match textField "<branch>" "name" root with
                | Error diagnostic -> Error diagnostic
                | Ok name when name <> "main" -> error "<branch>" "branch name is not exact main"
                | Ok _ ->
                    match property "protected" root with
                    | Some value when value.ValueKind = JsonValueKind.True ->
                        match property "commit" root with
                        | Some commit when commit.ValueKind = JsonValueKind.Object ->
                            match textField "<commit>" "sha" commit with
                            | Error diagnostic -> Error diagnostic
                            | Ok oid when not (Regex.IsMatch(oid, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant)) ->
                                error "<commit>" "branch commit SHA-1 must be 40 lowercase hexadecimal characters"
                            | Ok oid ->
                                Ok { RepositoryNodeId = repository.RepositoryNodeId
                                     RepositoryFullName = repository.RepositoryFullName
                                     CommitId = oid }
                        | _ -> error "<branch>" "commit object is absent or null"
                    | _ -> error "<branch>" "main branch protected fact is absent or false"
        with :? JsonException ->
            error "<branch>" "branch response JSON is malformed"

    /// Read the exact main branch tip; refuse any missing protection or HTTP provenance fact.
    /// This records one observed protected tip, not an immutable acceptance receipt or a
    /// guarantee that main remains at that tip. GraphQL repository-ID membership follows later.
    let inspectProvisionalPin
        (repository: ExactRepository)
        (reader: IReadOnlyProtectedBranchReader)
        : Result<GitCommitProvenance.ExactCommitPin, SyntaxDiagnostic> =
        if isNull (box repository)
           || String.IsNullOrWhiteSpace repository.RepositoryNodeId
           || isNull repository.RepositoryFullName
           || not (Regex.IsMatch(repository.RepositoryFullName, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
           || (repository.RepositoryFullName.Split('/') |> Array.exists (validSegment >> not)) then
            error "<repository>" "exact repository identity is absent or malformed"
        elif isNull (box reader) then
            error "<branch>" "read-only protected branch reader is unavailable"
        else
            let names = repository.RepositoryFullName.Split('/')
            let request =
                { Owner = names.[0]
                  Name = names.[1]
                  Branch = "main"
                  Url = sprintf "https://api.github.com/repos/%s/%s/branches/main" names.[0] names.[1] }
            let response =
                try reader.ReadExact request
                with _ -> Error ()
            match response with
            | Error () -> error "<branch>" "read-only protected branch response is unavailable"
            | Ok value when isNull (box value) -> error "<branch>" "protected branch response is absent"
            | Ok value when value.StatusCode <> 200 -> error "<branch>" "protected branch HTTP status is not 200"
            | Ok value when not (String.Equals(value.ResponseUrl, request.Url, StringComparison.Ordinal)) ->
                error "<branch>" "protected branch response origin differs from exact request"
            | Ok value when not (String.Equals(value.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                                 || String.Equals(value.MediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase)) ->
                error "<branch>" "protected branch response media type is not GitHub JSON"
            | Ok value when isNull value.Body || Array.isEmpty value.Body ->
                error "<branch>" "protected branch body is absent"
            | Ok value when value.Body.Length > 65536 ->
                error "<branch>" "protected branch body exceeds bounded size"
            | Ok value -> parsePin repository (Array.copy value.Body)
