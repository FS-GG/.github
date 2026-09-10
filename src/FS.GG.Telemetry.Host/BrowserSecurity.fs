namespace FS.GG.Telemetry.Host

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading

type BrowserPrincipalConfig =
    { PrincipalId: string
      KeyHashFile: string
      WorkspaceIds: string array
      Revoked: bool }

type BrowserOptions =
    { PublicOrigin: Uri
      IdleLifetime: TimeSpan
      AbsoluteLifetime: TimeSpan
      MaximumSessions: int
      LoginAttemptsPerMinute: int
      LoginAdmission: int
      QueryAdmission: int
      QueryTimeout: TimeSpan }

type BrowserIdentity =
    { PrincipalId: string
      WorkspaceIds: Set<string> }

type BrowserLoginResult =
    | LoginAccepted of sessionId: string * BrowserIdentity
    | LoginDenied
    | LoginOverloaded

module BrowserSecurity =
    let private safeId = Regex("\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z",RegexOptions.CultureInvariant)
    let private forbiddenMode =
        UnixFileMode.GroupRead ||| UnixFileMode.GroupWrite ||| UnixFileMode.GroupExecute
        ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherWrite ||| UnixFileMode.OtherExecute

    let validateOptions options =
        let errors=ResizeArray<string>()
        let origin=options.PublicOrigin
        if isNull origin || not origin.IsAbsoluteUri || origin.Scheme<>Uri.UriSchemeHttps
           || not(String.IsNullOrEmpty origin.UserInfo) || not(String.IsNullOrEmpty origin.Query)
           || not(String.IsNullOrEmpty origin.Fragment) || (origin.AbsolutePath<>"/" && origin.AbsolutePath<>"") then
            errors.Add "browser public origin must be one HTTPS origin"
        if options.IdleLifetime<TimeSpan.FromMinutes 1. || options.IdleLifetime>TimeSpan.FromHours 1. then errors.Add "browser idle lifetime is outside bounds"
        if options.AbsoluteLifetime<options.IdleLifetime || options.AbsoluteLifetime>TimeSpan.FromHours 12. then errors.Add "browser absolute lifetime is outside bounds"
        if options.MaximumSessions<1 || options.MaximumSessions>1024 then errors.Add "browser session capacity is outside bounds"
        if options.LoginAttemptsPerMinute<1 || options.LoginAttemptsPerMinute>256 then errors.Add "browser login rate is outside bounds"
        if options.LoginAdmission<1 || options.LoginAdmission>32 then errors.Add "browser login admission is outside bounds"
        if options.QueryAdmission<1 || options.QueryAdmission>32 then errors.Add "browser query admission is outside bounds"
        if options.QueryTimeout<TimeSpan.FromSeconds 1. || options.QueryTimeout>TimeSpan.FromSeconds 30. then errors.Add "browser query timeout is outside bounds"
        if errors.Count=0 then Ok options else Error(List.ofSeq errors)

    let private keyHash (path:string) =
        try
            if not(RuntimeInformation.IsOSPlatform OSPlatform.Linux) || not(Path.IsPathFullyQualified path) || not(File.Exists path) then Error "browser key hash file is unavailable" else
            let info=FileInfo path
            if not(isNull info.LinkTarget) || info.Length>1024L || (File.GetUnixFileMode(path) &&& forbiddenMode)<>enum 0 then Error "browser key hash file is unsafe" else
            let bytes=File.ReadAllBytes path
            use document=JsonDocument.Parse(bytes,JsonDocumentOptions(MaxDepth=3))
            let root=document.RootElement
            if root.ValueKind<>JsonValueKind.Object then Error "browser key hash file is invalid" else
            let names=root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
            if names.Length<>3 || names|>Array.distinct|>Array.length<>3 || Set.ofArray names<>set["schema";"algorithm";"keyHash"] then Error "browser key hash file is invalid" else
            let scalar (name:string) = match root.TryGetProperty name with true,value when value.ValueKind=JsonValueKind.String->value.GetString() | _->""
            let digest=scalar "keyHash"
            if scalar "schema"<>"fsgg.telemetry.browser-key/1" || scalar "algorithm"<>"sha256" || digest.Length<>64 || not(Regex.IsMatch(digest,"\A[0-9a-f]{64}\z",RegexOptions.CultureInvariant)) then Error "browser key hash file is invalid"
            else Ok(Convert.FromHexString digest)
        with :? IOException -> Error "browser key hash file is unavailable"
           | :? UnauthorizedAccessException -> Error "browser key hash file is unavailable"
           | :? JsonException -> Error "browser key hash file is invalid"
           | :? FormatException -> Error "browser key hash file is invalid"

    let validatePrincipals (principals:BrowserPrincipalConfig array) =
        let errors=ResizeArray<string>()
        if principals.Length>256 then errors.Add "browser principal capacity exceeded"
        let ids=HashSet<string>(StringComparer.Ordinal)
        for principal:BrowserPrincipalConfig in principals do
            if not(safeId.IsMatch principal.PrincipalId) || not(ids.Add principal.PrincipalId) then errors.Add "browser principal identity is invalid"
            if principal.WorkspaceIds.Length=0 || principal.WorkspaceIds.Length>256
               || principal.WorkspaceIds |> Array.exists(safeId.IsMatch >> not)
               || principal.WorkspaceIds.Length<>(principal.WorkspaceIds|>Array.distinct|>Array.length) then errors.Add "browser workspace allowlist is invalid"
            match keyHash principal.KeyHashFile with Error error->errors.Add error | Ok _->()
        if errors.Count=0 then Ok principals else Error(List.ofSeq errors)

    type private Principal = { Identity:BrowserIdentity; Hash:byte array; Revoked:bool }
    type private Session = { Identity:BrowserIdentity; Created:DateTimeOffset; mutable LastSeen:DateTimeOffset }

    [<Sealed>]
    type Service(options:BrowserOptions,configs:BrowserPrincipalConfig array) =
        let options=validateOptions options |> Result.defaultWith(fun errors->invalidArg "options" (String.concat ";" errors))
        let configs=validatePrincipals configs |> Result.defaultWith(fun errors->invalidArg "configs" (String.concat ";" errors))
        let principals =
            configs |> Array.map(fun (config:BrowserPrincipalConfig) ->
                let hash=keyHash config.KeyHashFile |> Result.defaultWith invalidOp
                config.PrincipalId,{Identity={PrincipalId=config.PrincipalId;WorkspaceIds=Set.ofArray config.WorkspaceIds};Hash=hash;Revoked=config.Revoked}) |> Map.ofArray
        let sessions=Dictionary<string,Session>(StringComparer.Ordinal)
        let aliases=Dictionary<string,string>(StringComparer.Ordinal)
        let sessionGate=obj()
        let loginAdmission=new SemaphoreSlim(options.LoginAdmission,options.LoginAdmission)
        let queryAdmission=new SemaphoreSlim(options.QueryAdmission,options.QueryAdmission)
        let rateGate=obj()
        let attempts=Queue<DateTimeOffset>()
        let sessionKey (value:string) = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes value))
        let freshId () = Convert.ToBase64String(RandomNumberGenerator.GetBytes 32).TrimEnd('=').Replace('+','-').Replace('/','_')
        let accessKeyHash (value:string) =
            try
                if isNull value || value.Length<43 || value.Length>86
                   || value |> Seq.exists(fun c->not(Char.IsAsciiLetterOrDigit c || c='-' || c='_')) then None else
                let padded=value.Replace('-','+').Replace('_','/')+String('=',(4-value.Length%4)%4)
                let material=Convert.FromBase64String padded
                if material.Length<32 || material.Length>64 then
                    CryptographicOperations.ZeroMemory material
                    None
                else
                let digest=SHA256.HashData material
                CryptographicOperations.ZeroMemory material
                Some digest
            with :? FormatException -> None
        let active now (session:Session) = now-session.LastSeen<=options.IdleLifetime && now-session.Created<=options.AbsoluteLifetime
        let removeSessionLocked key =
            sessions.Remove key |> ignore
            aliases
            |> Seq.choose(fun pair->if pair.Key=key || pair.Value=key then Some pair.Key else None)
            |> Seq.toArray
            |> Array.iter(fun alias->aliases.Remove alias|>ignore)
        let rateAdmitted now = lock rateGate (fun () ->
            while attempts.Count>0 && now-attempts.Peek()>=TimeSpan.FromMinutes 1. do attempts.Dequeue() |> ignore
            if attempts.Count>=options.LoginAttemptsPerMinute then false else attempts.Enqueue now;true)
        let findLocked sessionId now touch =
            if String.IsNullOrWhiteSpace sessionId || sessionId.Length<>43 then None else
            let key=sessionKey sessionId
            match sessions.TryGetValue key with
            | true,session when active now session ->
                if touch then session.LastSeen<-now
                Some session
            | true,_ -> removeSessionLocked key;None
            | _ -> None
        member _.LoginAcquired(principalId,accessKey,now) =
                if not(rateAdmitted now) then LoginOverloaded else
                let parsed=accessKeyHash accessKey
                let supplied = parsed |> Option.defaultValue(Array.zeroCreate 32)
                let principal=principals.TryFind principalId
                let expected=principal |> Option.map _.Hash |> Option.defaultValue(Array.zeroCreate 32)
                let matches=CryptographicOperations.FixedTimeEquals(ReadOnlySpan supplied,ReadOnlySpan expected)
                CryptographicOperations.ZeroMemory supplied
                match principal with
                | Some value when parsed.IsSome && matches && not value.Revoked ->
                    lock sessionGate (fun () ->
                        let expired=sessions |> Seq.choose(fun pair->if active now pair.Value then None else Some pair.Key) |> Seq.toArray
                        expired |> Array.iter removeSessionLocked
                        if sessions.Count>=options.MaximumSessions then LoginOverloaded else
                        let id=freshId()
                        sessions.Add(sessionKey id,{Identity=value.Identity;Created=now;LastSeen=now})
                        LoginAccepted(id,value.Identity))
                | _ -> LoginDenied
        member this.Login(principalId,accessKey,now) =
            if not(loginAdmission.Wait 0) then LoginOverloaded else
            try this.LoginAcquired(principalId,accessKey,now)
            finally loginAdmission.Release() |> ignore
        member _.TryAcquireLogin()=loginAdmission.Wait 0
        member _.ReleaseLogin()=loginAdmission.Release() |> ignore
        member _.Validate(sessionId,now) = lock sessionGate (fun()->findLocked sessionId now true |> Option.map _.Identity)
        member _.Rotate(sessionId,now) =
            lock sessionGate (fun()->
                match findLocked sessionId now false with
                | None -> None
                | Some session ->
                    let oldKey=sessionKey sessionId
                    aliases |> Seq.choose(fun pair->if pair.Value=oldKey then Some pair.Key else None) |> Seq.toArray |> Array.iter(fun key->aliases.Remove key|>ignore)
                    sessions.Remove oldKey |> ignore
                    let replacement=freshId()
                    let replacementKey=sessionKey replacement
                    sessions.Add(replacementKey,{session with LastSeen=now})
                    aliases[oldKey]<-replacementKey
                    Some(replacement,session.Identity))
        member _.Logout(sessionId) =
            if not(String.IsNullOrWhiteSpace sessionId) then lock sessionGate (fun()->
                let key=sessionKey sessionId
                let target=match aliases.TryGetValue key with true,value->value | _->key
                sessions.Remove target |> ignore
                aliases |> Seq.choose(fun pair->if pair.Key=key || pair.Value=target then Some pair.Key else None) |> Seq.toArray |> Array.iter(fun alias->aliases.Remove alias|>ignore))
        member _.SessionCount=lock sessionGate (fun()->sessions.Count)
        member internal _.AliasCount=lock sessionGate (fun()->aliases.Count)
        member _.TryAcquireQuery()=queryAdmission.Wait 0
        member _.ReleaseQuery()=queryAdmission.Release() |> ignore
        interface IDisposable with member _.Dispose()=loginAdmission.Dispose();queryAdmission.Dispose();sessions.Clear();aliases.Clear()
