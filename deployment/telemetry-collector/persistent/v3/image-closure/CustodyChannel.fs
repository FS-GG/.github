namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Net.Sockets
open Microsoft.Win32.SafeHandles

// Exact commands remain selected by RootlessMechanism, never by the transport.
type ProcessCommand={Executable:string;Arguments:string array;Environment:Map<string,string>;WorkingDirectory:string;OutputLimit:int}
type ProcessOutput={ExitCode:int;Stdout:string;Stderr:string}
type IOwnedProcess=
    abstract Identity:string
    abstract Wait:CancellationToken->ProcessOutput
    abstract Cancel:CancellationToken->unit
    abstract HasExited:bool
    inherit IDisposable
type IProcessBackend=
    abstract Start:ProcessCommand->IOwnedProcess
    abstract ReadBuilderIdentity:unit->string
    abstract ReadSupervisorIdentity:unit->string
    abstract ReadAvailableCapacity:string->int64
    abstract RetainCommandEvidence:byte array->unit

module CustodyProtocol=
    [<Literal>]
    let Schema="fsgg.learn.command-custody/1"
    let require condition reason=if not condition then raise(IOException reason)
    let exact names (node:JsonElement)=
        require(node.ValueKind=JsonValueKind.Object) "custody-object"
        let actual=node.EnumerateObject()|>Seq.map _.Name|>Seq.toArray
        require(actual.Length=List.length names&&Set.ofArray actual=Set.ofList names) "custody-fields"
    let sha (value:string)=require(System.Text.RegularExpressions.Regex.IsMatch(value,"^[a-f0-9]{64}$")) "custody-sha256"
    let binding (node:JsonElement)=
        exact ["schema";"identity";"deadline";"admissionSha256"] node
        require(node.GetProperty("schema").GetString()=Schema) "custody-schema"
        sha(node.GetProperty("identity").GetString());sha(node.GetProperty("admissionSha256").GetString())
        let deadline=DateTimeOffset.Parse(node.GetProperty("deadline").GetString(),Globalization.CultureInfo.InvariantCulture)
        require(deadline>DateTimeOffset.UtcNow&&deadline<=DateTimeOffset.UtcNow.AddMinutes 90.) "custody-original-deadline"
        deadline

/// One inherited AF_UNIX stream. Loss/partial framing poisons the lease; no reconnect or legacy fallback.
type CustodyChannel private(socket:Socket,identity:string,deadline:DateTimeOffset,admissionSha256:string)=
    let gate=obj()
    let stream=new NetworkStream(socket,false)
    let mutable ordinal=0
    let mutable failed=false
    let mutable inputBound=false
    let leases=Collections.Generic.HashSet<string>()
    let exchange action payload (token:CancellationToken)=lock gate (fun()->
        CustodyProtocol.require(not failed&&DateTimeOffset.UtcNow<deadline) "custody-lost-or-expired"
        token.ThrowIfCancellationRequested()
        ordinal<-ordinal+1
        let bytes=JsonSerializer.SerializeToUtf8Bytes {|schema=CustodyProtocol.Schema;identity=identity;ordinal=ordinal;action=action;payload=payload|}
        CustodyProtocol.require(bytes.Length<=2*1024*1024) "custody-frame-bound"
        use wait=CancellationTokenSource.CreateLinkedTokenSource token
        wait.CancelAfter(deadline-DateTimeOffset.UtcNow)
        try
            let header=BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder bytes.Length)
            stream.WriteAsync(header.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            stream.WriteAsync(bytes.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            let responseHeader=Array.zeroCreate<byte> 4
            stream.ReadExactlyAsync(responseHeader.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            let count=System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(responseHeader,0))
            CustodyProtocol.require(count>0&&count<=2*1024*1024) "custody-response-bound"
            let response=Array.zeroCreate<byte> count
            stream.ReadExactlyAsync(response.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            use document=JsonDocument.Parse response
            let node=document.RootElement
            CustodyProtocol.exact ["schema";"identity";"ordinal";"status";"payload"] node
            CustodyProtocol.require(node.GetProperty("schema").GetString()=CustodyProtocol.Schema&&node.GetProperty("identity").GetString()=identity&&node.GetProperty("ordinal").GetInt32()=ordinal) "custody-reply-join"
            CustodyProtocol.require(node.GetProperty("status").GetString()="acknowledged") "custody-unknown"
            node.GetProperty("payload").Clone()
        with _->failed<-true;reraise())
    member _.Identity=identity
    member _.Deadline=deadline
    member _.BindInput input=
        CustodyProtocol.sha input
        CustodyProtocol.require(not inputBound) "custody-input-already-bound"
        let reply=exchange "bind-input" {|input=input;admissionSha256=admissionSha256|} CancellationToken.None
        CustodyProtocol.exact ["input"] reply
        CustodyProtocol.require(reply.GetProperty("input").GetString()=input) "custody-input-join"
        inputBound<-true
    member _.Retain(bytes:byte array)=
        let reply=exchange "retain" {|bytes=bytes.Length|} CancellationToken.None
        CustodyProtocol.exact ["charged"] reply
        CustodyProtocol.require(reply.GetProperty("charged").GetInt32()=bytes.Length) "custody-output-charge"
    member _.Start(command:ProcessCommand)=
        CustodyProtocol.require(inputBound&&command.Executable="/usr/sbin/podman"&&command.OutputLimit=1024*1024) "custody-command-not-selected"
        let reply=exchange "start" command CancellationToken.None
        CustodyProtocol.exact ["lease"] reply
        let lease=reply.GetProperty("lease").GetString()
        CustodyProtocol.sha lease
        CustodyProtocol.require(leases.Add lease) "custody-lease-reused"
        let mutable retired=false
        let mutable result:ProcessOutput option=None
        let observe action token=
            let response=exchange action {|lease=lease|} token
            CustodyProtocol.exact ["lease";"retired";"exitCode";"stdout";"stderr"] response
            CustodyProtocol.require(response.GetProperty("lease").GetString()=lease) "custody-command-join"
            if response.GetProperty("retired").GetBoolean() then
                retired<-true
                result<-Some {ExitCode=response.GetProperty("exitCode").GetInt32();Stdout=response.GetProperty("stdout").GetString();Stderr=response.GetProperty("stderr").GetString()}
        {new IOwnedProcess with
            member _.Identity=identity+":"+lease
            member _.HasExited=retired&&not failed
            member _.Wait token=
                while not retired do observe "observe" token
                result.Value
            member _.Cancel token=
                if not retired then observe "cancel" token
                while not retired do observe "observe" token
            member _.Dispose()=
                // Disposal never manufactures retirement, and never selects a numeric process.
                if not retired then failed<-true}
    interface IDisposable with member _.Dispose()=stream.Dispose();socket.Dispose()
    static member BindInherited()=
        let raw=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_FD"
        let selected=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_BINDING"
        CustodyProtocol.require(not(String.IsNullOrWhiteSpace raw)&&not(String.IsNullOrWhiteSpace selected)) "root-admitted-custody-channel-required"
        let fd=Int32.Parse raw
        CustodyProtocol.require(fd>=3) "custody-inherited-fd"
        let bytes=Convert.FromBase64String selected
        CustodyProtocol.require(bytes.Length<=4096) "custody-binding-bound"
        use doc=JsonDocument.Parse bytes
        let deadline=CustodyProtocol.binding doc.RootElement
        let socket=new Socket(new SafeSocketHandle(nativeint fd,true))
        try
            CustodyProtocol.require(socket.AddressFamily=AddressFamily.Unix&&socket.SocketType=SocketType.Stream&&socket.Connected) "custody-local-stream-required"
            new CustodyChannel(socket,doc.RootElement.GetProperty("identity").GetString(),deadline,doc.RootElement.GetProperty("admissionSha256").GetString())
        with _->socket.Dispose();reraise()
