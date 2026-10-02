namespace Fs.Gg.Telemetry.HostAttempt

open System
open System.Buffers.Binary
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

[<CLIMutable>]
type ConcreteRunRequest =
    { Preparation: PreparationRequest
      ProducerSha256: string
      TransportSha256: string
      InvocationId: string
      LeaseId: string
      AuthChannel: string
      OwnershipChannel: string
      VerifierChannel: string }

type RootChannels = { Auth: Stream; Ownership: Stream; Verifier: Stream }
type RunRecord = { Id:string; HeadSha:string; Event:string; Status:string; Conclusion:string option; CreatedAt:DateTimeOffset; RunAttempt:int }
type HttpEnvelope = { Status:int; Headers:Map<string,string>; Body:byte array }
type AuthReceipt = { Sequence:int; Repository:string; Environment:string; RequestedRole:string }
type OwnershipReceipt = { Sequence:int; RunId:string; RunAttempt:int; Repository:string; Workflow:string; QualificationRef:string; PlacementSha:string; Nonce:string; Event:string }
type VerifierReceipt = { Sequence:int;RunId:string;RunAttempt:int;Nonce:string;ProfileSha256:string;BindingSha256:string;SourceSha:string;ArtifactName:string;ResultBytes:byte array;ResultSha256:string;VerifierExitCode:int;VerifierReceiptBytes:byte array;VerifierReceiptSha256:string }

module AttemptAcquisition =
    [<Literal>]
    let MaxFrameBytes=1048576
    [<Literal>]
    let MaxHttpBodyBytes=1048576
    [<Literal>]
    let MaxRunRecords=20
    let private refuse value=raise(AttemptRefusal value)
    let private require condition value=if not condition then refuse value
    let shaBytes (bytes:byte array)=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    let shaFile path=use stream=File.OpenRead path in Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()
    let regularFile path maximum =
        let info=FileInfo(Path.GetFullPath path)
        require(info.Exists && info.Length>0L && info.Length<=maximum && not(info.Attributes.HasFlag FileAttributes.ReparsePoint)) "acquired-file-refused"
        let before=info.Length,info.LastWriteTimeUtc
        let bytes=File.ReadAllBytes info.FullName
        info.Refresh();require(before=(info.Length,info.LastWriteTimeUtc)) "acquired-file-replaced"
        bytes
    let document bytes = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes),JsonDocumentOptions(CommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false,MaxDepth=32))
    let fields (element:JsonElement)=
        require(element.ValueKind=JsonValueKind.Object) "acquired-json-object-refused"
        let seen=HashSet<string>(StringComparer.Ordinal)
        [for p in element.EnumerateObject() do require(seen.Add p.Name) "acquired-json-duplicate-refused";yield p.Name,p.Value]|>Map.ofList
    let keys expected (v:Map<string,JsonElement>)=require(Set.ofList expected=Set.ofSeq v.Keys) "acquired-json-fields-refused"
    let requiredKeys expected (v:Map<string,JsonElement>)=require(Set.isSubset(Set.ofList expected)(Set.ofSeq v.Keys)) "acquired-json-fields-refused"
    let text name (v:Map<string,JsonElement>)=require(v[name].ValueKind=JsonValueKind.String) "acquired-json-text-refused";v[name].GetString()
    let integer name (v:Map<string,JsonElement>)=let mutable n=0 in require(v[name].ValueKind=JsonValueKind.Number&&v[name].TryGetInt32(&n)) "acquired-json-integer-refused";n
    let readFrameWithCancellation (stream:Stream) remainingSeconds (interrupted:CancellationToken) =
        require(remainingSeconds>0) "root-channel-deadline"
        use deadline=new CancellationTokenSource(TimeSpan.FromSeconds(float remainingSeconds))
        use timeout=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,interrupted)
        let readExact (buffer:byte array) =
            let mutable offset=0
            while offset<buffer.Length do
                let count=
                    try
                        let completion=TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
                        let reader=Thread(ThreadStart(fun()->try completion.TrySetResult(stream.Read(buffer,offset,buffer.Length-offset))|>ignore with error->completion.TrySetException error|>ignore))
                        reader.IsBackground<-true
                        reader.Start()
                        completion.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult()
                    with
                    | :? OperationCanceledException when interrupted.IsCancellationRequested ->
                        raise (OperationCanceledException("root-channel-interrupted", interrupted))
                    | :? OperationCanceledException -> refuse "root-channel-deadline"
                if count=0 then refuse "root-channel-interrupted"
                offset<-offset+count
        let prefix=Array.zeroCreate<byte> 4
        readExact prefix
        let length=BinaryPrimitives.ReadInt32BigEndian(prefix)
        require(length>0 && length<=MaxFrameBytes) "root-channel-frame-refused"
        let payload=Array.zeroCreate<byte> length
        readExact payload;payload
    let readFrame stream remainingSeconds=readFrameWithCancellation stream remainingSeconds CancellationToken.None

    let private common expectedSchema invocation destination expectedSequence bytes =
        use doc=document bytes
        let v=fields(doc.RootElement.Clone())
        require(text "schema" v=expectedSchema && text "invocationId" v=invocation && text "destinationId" v=destination && integer "sequence" v=expectedSequence) "root-channel-binding-refused"
        v
    let auth invocation destination sequence bytes =
        let v=common "fsgg.telemetry.host-attempt-auth-metadata/1" invocation destination sequence bytes
        keys ["schema";"invocationId";"destinationId";"sequence";"repository";"environment";"requestedRole"] v
        {Sequence=sequence;Repository=text "repository" v;Environment=text "environment" v;RequestedRole=text "requestedRole" v}
    let ownership invocation destination sequence bytes =
        let v=common "fsgg.telemetry.host-attempt-owned-run/1" invocation destination sequence bytes
        keys ["schema";"invocationId";"destinationId";"sequence";"runId";"runAttempt";"repository";"workflow";"qualificationRef";"placementSha";"nonce";"event"] v
        {Sequence=sequence;RunId=text "runId" v;RunAttempt=integer "runAttempt" v;Repository=text "repository" v;Workflow=text "workflow" v;QualificationRef=text "qualificationRef" v;PlacementSha=text "placementSha" v;Nonce=text "nonce" v;Event=text "event" v}
    let verifier invocation destination sequence bytes =
        let v=common "fsgg.telemetry.host-attempt-verifier-receipt/1" invocation destination sequence bytes
        keys ["schema";"invocationId";"destinationId";"sequence";"runId";"runAttempt";"nonce";"profileSha256";"bindingSha256";"sourceSha";"artifactName";"resultBase64";"resultSha256";"verifierExitCode";"verifierReceiptBase64";"verifierReceiptSha256"] v
        let result=try Convert.FromBase64String(text "resultBase64" v) with _->refuse "verifier-result-encoding-refused"
        let custody=try Convert.FromBase64String(text "verifierReceiptBase64" v) with _->refuse "verifier-receipt-encoding-refused"
        require(result.Length>0&&result.Length<=MaxFrameBytes&&shaBytes result=text "resultSha256" v) "verifier-result-digest-refused"
        require(custody.Length>0&&custody.Length<=MaxFrameBytes&&shaBytes custody=text "verifierReceiptSha256" v) "verifier-receipt-digest-refused"
        {Sequence=sequence;RunId=text "runId" v;RunAttempt=integer "runAttempt" v;Nonce=text "nonce" v;ProfileSha256=text "profileSha256" v;BindingSha256=text "bindingSha256" v;SourceSha=text "sourceSha" v;ArtifactName=text "artifactName" v;ResultBytes=result;ResultSha256=text "resultSha256" v;VerifierExitCode=integer "verifierExitCode" v;VerifierReceiptBytes=custody;VerifierReceiptSha256=text "verifierReceiptSha256" v}

    let http (raw:string) =
        let normalized=raw.Replace("\r\n","\n",StringComparison.Ordinal)
        let split=normalized.IndexOf("\n\n",StringComparison.Ordinal)
        require(split>0) "http-framing-refused"
        let headerLines=normalized.Substring(0,split).Split('\n')
        let statusParts=headerLines[0].Split(' ',StringSplitOptions.RemoveEmptyEntries)
        let mutable status=0
        require(statusParts.Length>=2 && statusParts[0].StartsWith("HTTP/",StringComparison.Ordinal) && Int32.TryParse(statusParts[1],&status)) "http-status-refused"
        let mutable headers=Map.empty
        for line in headerLines[1..] do
            let colon=line.IndexOf(':')
            require(colon>0) "http-header-refused"
            let name=line.Substring(0,colon).Trim().ToLowerInvariant()
            require(Regex.IsMatch(name,"\\A[a-z0-9-]{1,64}\\z") && not(headers.ContainsKey name)) "http-header-refused"
            headers<-headers.Add(name,line.Substring(colon+1).Trim())
        let body=Encoding.UTF8.GetBytes(normalized.Substring(split+2))
        require(body.Length<=MaxHttpBodyBytes) "http-body-limit"
        {Status=status;Headers=headers;Body=body}
    let private requiredNoNext envelope =
        require(envelope.Status=200) "http-success-required"
        match envelope.Headers.TryFind "link" with Some value when value.Contains("rel=\"next\"",StringComparison.OrdinalIgnoreCase)->refuse "http-pagination-incomplete"|_->()
    let responseDate envelope =
        let value=envelope.Headers.TryFind "date"|>Option.defaultWith(fun()->refuse "http-date-missing")
        let mutable parsed=DateTimeOffset.MinValue
        require(DateTimeOffset.TryParseExact(value,"r",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,&parsed)) "http-date-refused"
        parsed
    let repository expected envelope =
        requiredNoNext envelope
        use doc=document envelope.Body
        let v=fields doc.RootElement
        require(v.ContainsKey "full_name" && text "full_name" v=expected) "repository-identity-refused"
    let binding expected envelopeBytes =
        use doc=document envelopeBytes
        let v=fields doc.RootElement
        keys ["schema";"recipeSourceSha";"recipeSourceTree";"profileSha256";"operationId";"sourcePinsSha256";"producerSha256";"bindingSha256"] v
        require(text "schema" v="fsgg.telemetry.host-binding-render/1" && text "bindingSha256" v=expected) "binding-result-refused"
    let runs envelope =
        requiredNoNext envelope
        use doc=document envelope.Body
        let root=fields doc.RootElement
        keys ["total_count";"workflow_runs"] root
        require(root["workflow_runs"].ValueKind=JsonValueKind.Array) "run-array-refused"
        let records=[for item in root["workflow_runs"].EnumerateArray() do
                        let v=fields item
                        requiredKeys ["id";"head_sha";"event";"status";"conclusion";"created_at";"run_attempt"] v
                        let id=match v["id"].ValueKind with JsonValueKind.Number->v["id"].GetInt64().ToString(CultureInfo.InvariantCulture)|JsonValueKind.String->text "id" v|_->refuse "run-id-refused"
                        let conclusion=if v["conclusion"].ValueKind=JsonValueKind.Null then None else Some(text "conclusion" v)
                        let mutable created=DateTimeOffset.MinValue
                        require(DateTimeOffset.TryParseExact(text "created_at" v,"yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,&created)) "run-time-refused"
                        yield {Id=id;HeadSha=text "head_sha" v;Event=text "event" v;Status=text "status" v;Conclusion=conclusion;CreatedAt=created;RunAttempt=integer "run_attempt" v}]
        let total=integer "total_count" root
        require(records.Length<=MaxRunRecords && total=records.Length && records|>List.map _.Id|>List.distinct|>List.length=records.Length) "run-census-refused"
        records
    let run envelope =
        require(envelope.Status=200) "run-http-refused"
        use doc=document envelope.Body
        let v=fields doc.RootElement
        requiredKeys ["id";"head_sha";"event";"status";"conclusion";"run_attempt"] v
        let id=match v["id"].ValueKind with JsonValueKind.Number->v["id"].GetInt64().ToString(CultureInfo.InvariantCulture)|JsonValueKind.String->text "id" v|_->refuse "run-id-refused"
        let conclusion=if v["conclusion"].ValueKind=JsonValueKind.Null then None else Some(text "conclusion" v)
        {Id=id;HeadSha=text "head_sha" v;Event=text "event" v;Status=text "status" v;Conclusion=conclusion;CreatedAt=DateTimeOffset.MinValue;RunAttempt=integer "run_attempt" v}
    let exactRun expectedId expectedHead expectedEvent expectedAttempt acquired =
#if HOST_ATTEMPT_REMOVE_PROVIDER_IDENTITY_GUARD
        acquired
#else
        require(acquired.Id=expectedId&&acquired.HeadSha=expectedHead&&acquired.Event=expectedEvent&&acquired.RunAttempt=expectedAttempt) "provider-run-identity-refused"
        acquired
#endif
    let secrets envelope =
        requiredNoNext envelope
        use doc=document envelope.Body
        let v=fields doc.RootElement
        keys ["total_count";"secrets"] v
        require(v["secrets"].ValueKind=JsonValueKind.Array) "secret-array-refused"
        let names=[for item in v["secrets"].EnumerateArray() do let p=fields item in requiredKeys ["name";"updated_at"] p;yield text "name" p]
        require(integer "total_count" v=names.Length && List.distinct names|>List.length=names.Length) "secret-census-refused";names

    let canonicalNative expectedOperation expectedNonce bytes =
        use doc=document bytes
        let v=fields doc.RootElement
        keys ["schema";"status";"operationId";"runNonce";"parentThreadId";"childThreadId";"nativeAgent";"nativeAgentPath";"spawnCount";"childTerminalTurns";"protocolMode";"originalRolloutAudit";"waitCount";"followups";"automaticRetries";"childTerminalEvidence";"observedNotifications";"authoritativeHistory";"events"] v
        let bounded name maximum = let value=text name v in require(value.Length>0&&value.Length<=maximum) "native-result-scalar-refused";value
        require(text "schema" v="fsgg.telemetry.native-operation-result/1"&&text "status" v="qualified"&&text "operationId" v=expectedOperation&&text "runNonce" v=expectedNonce) "native-result-join-refused"
        for name in ["parentThreadId";"childThreadId"] do require(Guid.TryParse(bounded name 64)|>fst) "native-result-identity-refused"
        for name in ["nativeAgent";"nativeAgentPath";"protocolMode";"originalRolloutAudit";"childTerminalEvidence"] do bounded name 256|>ignore
        let count name=integer name v
        require(count "spawnCount"=1&&count "childTerminalTurns">=1&&count "waitCount">=1&&count "followups"=0&&count "automaticRetries"=0) "native-result-count-refused"
        let notifications=fields v["observedNotifications"]
        keys ["childAcknowledgements";"parentAcknowledgements";"childTerminal"] notifications
        require(integer "childAcknowledgements" notifications>0&&integer "parentAcknowledgements" notifications>0&&integer "childTerminal" notifications>0) "native-result-notifications-refused"
        let history=fields v["authoritativeHistory"]
        keys ["parentSpawn";"parentWait";"parentAcknowledgements";"childAcknowledgements";"prohibitedTools";"followups"] history
        require(integer "parentSpawn" history=1&&integer "parentWait" history>=1&&integer "parentAcknowledgements" history>0&&integer "childAcknowledgements" history>0&&integer "prohibitedTools" history=0&&integer "followups" history=0) "native-result-history-refused"
        require(v["events"].ValueKind=JsonValueKind.Array&&v["events"].GetArrayLength()>0&&v["events"].GetArrayLength()<=128) "native-result-events-refused"
        let mutable previous=0
        for item in v["events"].EnumerateArray() do
            let event=fields item
            requiredKeys ["sequence";"kind"] event
            let sequence=integer "sequence" event
            require(sequence=previous+1&&(text "kind" event).Length>0) "native-result-event-refused"
            previous<-sequence
