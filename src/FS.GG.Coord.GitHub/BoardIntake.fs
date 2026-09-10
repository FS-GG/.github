namespace FS.GG.Coord.GitHub

module BoardIntake =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Security.Cryptography
    open System.Text
    open System.Text.Json
    open Errors

    type Admission =
        { RepositoryId: string
          IssueId: string
          UpdatedAt: string
          AuthorId: string
          AuthorLogin: string
          Permission: string }

    [<CLIMutable>]
    type Timed<'a> = { Value: 'a; CheckedAt: DateTimeOffset; ExpiresAt: DateTimeOffset }

    type Gate(transport: Transport.IGitHubTransport, ?ttl: TimeSpan, ?cacheRoot: string, ?clock: unit -> DateTimeOffset) =
        let maximumTtl = TimeSpan.FromMinutes 2.
        let ttl = min (defaultArg ttl maximumTtl) maximumTtl
        let clock = defaultArg clock (fun () -> DateTimeOffset.UtcNow)
        let policies = Dictionary<string,Timed<OperationalGraphQl.RepositoryPolicy>>(StringComparer.OrdinalIgnoreCase)
        let permissions = Dictionary<string * string,Timed<string>>()
        let fresh now (entry: Timed<_>) = entry.CheckedAt <= now && entry.ExpiresAt > now && entry.ExpiresAt <= entry.CheckedAt + maximumTtl
        let timed (now: DateTimeOffset) value = { Value = value; CheckedAt = now; ExpiresAt = now + ttl }
        let cacheDir = Path.Combine(defaultArg cacheRoot (Cache.root()),"board-intake-v1")
        let key (value: string) = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes value)).ToLowerInvariant()
        let file kind value = Path.Combine(cacheDir,$"%s{kind}-%s{key value}.json")
        let prepare () =
            Directory.CreateDirectory cacheDir |> ignore
            if DirectoryInfo(cacheDir).LinkTarget <> null then invalidOp "board intake cache directory must not be a symbolic link"
            if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(cacheDir,UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
        let read path now project =
            try
                if not(File.Exists path) || FileInfo(path).Length > 16384L then None else
                if not (OperatingSystem.IsWindows()) && File.GetUnixFileMode(path) <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then None else
                use document = JsonDocument.Parse(File.ReadAllText path)
                let root = document.RootElement
                let checkedAt = DateTimeOffset.Parse(root.GetProperty("checkedAt").GetString())
                let expiresAt = DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString())
                let value = { Value = project root; CheckedAt = checkedAt; ExpiresAt = expiresAt }
                if not(fresh now value) then None else Some value
            with _ -> None
        let evict () =
            try
                Directory.EnumerateFiles(cacheDir,"*.json")
                |> Seq.map FileInfo
                |> Seq.sortByDescending _.LastWriteTimeUtc
                |> Seq.skip 256
                |> Seq.iter (fun item -> item.Delete())
            with _ -> ()
        let save path (value: Timed<_>) payload =
            try
                prepare()
                let temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
                let checkedAt = JsonSerializer.Serialize(value.CheckedAt.ToString("O"))
                let expiresAt = JsonSerializer.Serialize(value.ExpiresAt.ToString("O"))
                let json = $"{{\"checkedAt\":%s{checkedAt},\"expiresAt\":%s{expiresAt},%s{payload}}}"
                do
                    use stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)
                    let bytes = Encoding.UTF8.GetBytes json
                    stream.Write(bytes,0,bytes.Length)
                    stream.Flush(true)
                if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temp,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                File.Move(temp,path,true)
                evict()
            with _ -> () // cache failure buys a live read next time; it never authorizes

        let validPolicy (value: OperationalGraphQl.RepositoryPolicy) =
            not(String.IsNullOrWhiteSpace value.RepositoryId)
            && (value.IssueCreationPolicy = "ALL" || value.IssueCreationPolicy = "COLLABORATORS_ONLY")
        let trim (now: DateTimeOffset) (values: Dictionary<'key,Timed<'value>>) =
            values |> Seq.filter (fun pair -> not(fresh now pair.Value)) |> Seq.map _.Key |> Seq.toArray |> Array.iter (fun item -> values.Remove item |> ignore)
            if values.Count > 256 then
                values |> Seq.sortBy _.Value.CheckedAt |> Seq.take(values.Count-256) |> Seq.map _.Key |> Seq.toArray |> Array.iter (fun item -> values.Remove item |> ignore)

        member _.Authorize(owner,repo,number) =
            let now = clock()
            trim now policies
            trim now permissions
            let repoName = owner + "/" + repo
            let policy =
                match policies.TryGetValue repoName with
                | true, entry when fresh now entry -> Ok entry.Value
                | _ ->
                    let path = file "policy" (repoName.ToLowerInvariant())
                    match read path now (fun root -> ({ RepositoryId=root.GetProperty("repositoryId").GetString(); IssueCreationPolicy=root.GetProperty("issueCreationPolicy").GetString(); HasIssuesEnabled=root.GetProperty("hasIssuesEnabled").GetBoolean(); MergeCommitAllowed=root.GetProperty("mergeCommitAllowed").GetBoolean(); SquashMergeAllowed=root.GetProperty("squashMergeAllowed").GetBoolean(); RebaseMergeAllowed=root.GetProperty("rebaseMergeAllowed").GetBoolean() }: OperationalGraphQl.RepositoryPolicy)) with
                    | Some entry when validPolicy entry.Value -> policies[repoName] <- entry; Ok entry.Value
                    | _ -> OperationalGraphQl.repositoryPolicy transport owner repo |> Result.bind (fun value -> if not(validPolicy value) then Error(Malformed(repoName,"repository policy response is incomplete or unknown")) else let entry=timed now value in policies[repoName] <- entry; save path entry $"\"repositoryId\":{JsonSerializer.Serialize value.RepositoryId},\"issueCreationPolicy\":{JsonSerializer.Serialize value.IssueCreationPolicy},\"hasIssuesEnabled\":{JsonSerializer.Serialize value.HasIssuesEnabled},\"mergeCommitAllowed\":{JsonSerializer.Serialize value.MergeCommitAllowed},\"squashMergeAllowed\":{JsonSerializer.Serialize value.SquashMergeAllowed},\"rebaseMergeAllowed\":{JsonSerializer.Serialize value.RebaseMergeAllowed}"; Ok value)
            policy
            |> Result.bind (fun policy ->
                if policy.HasIssuesEnabled && not(String.Equals(policy.IssueCreationPolicy,"COLLABORATORS_ONLY",StringComparison.Ordinal)) then
                    Error(Malformed(repoName,$"issue creation policy is %s{policy.IssueCreationPolicy}; expected COLLABORATORS_ONLY"))
                else
                    OperationalGraphQl.issueIntakeIdentity transport owner repo number
                    |> Result.bind (fun issue ->
                        let permissionKey = policy.RepositoryId,issue.AuthorId
                        let permission =
                            match permissions.TryGetValue permissionKey with
                            | true, entry when fresh now entry -> Ok entry.Value
                            | _ ->
                                let path = file "permission" (policy.RepositoryId+"\u0000"+issue.AuthorId)
                                match read path now (fun root -> root.GetProperty("permission").GetString()) with
                                | Some entry when not(String.IsNullOrWhiteSpace entry.Value) && ["read";"triage";"write";"maintain";"admin"] |> List.contains(entry.Value.ToLowerInvariant()) -> permissions[permissionKey] <- entry; Ok entry.Value
                                | _ -> Reads.collaboratorPermission transport owner repo issue.AuthorLogin issue.AuthorId |> Result.map (fun value -> let entry=timed now value in permissions[permissionKey] <- entry; save path entry $"\"permission\":{JsonSerializer.Serialize value}"; value)
                        permission
                        |> Result.bind (fun permission ->
                            if not (["write";"maintain";"admin"] |> List.exists (fun allowed -> String.Equals(allowed,permission,StringComparison.OrdinalIgnoreCase))) then
                                Error(Malformed($"%s{repoName}#%d{number}",$"issue author %s{issue.AuthorLogin} has current repository permission '%s{permission}', below write"))
                            else
                                Ok { RepositoryId = policy.RepositoryId; IssueId = issue.IssueId; UpdatedAt = issue.UpdatedAt
                                     AuthorId = issue.AuthorId; AuthorLogin = issue.AuthorLogin; Permission = permission })))
