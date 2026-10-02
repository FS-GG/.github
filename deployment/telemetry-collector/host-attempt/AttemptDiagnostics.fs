namespace Fs.Gg.Telemetry.HostAttempt

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading

type internal DiagnosticStage = PreparationRender | PreparationDerive | Transport | TransportAdapter | Admission | EffectCheck | ActionValidation
type internal DiagnosticAction = ReadPublicIdentity | RequestBranchFastForward | InspectAuthMetadata | InvokeBinding | TransferNativeAuth | TransferEffectAdmission | DispatchOnce | ListRuns | GetRun | DownloadRunArtifacts | CancelOwnedRun | DeleteNativeAuth | DeleteEffectAdmission | ReadNativeAuthAbsence | ReadEffectAdmissionAbsence | EmitRootReadback
type internal DiagnosticOutcome = Returned | ResponseLost | Malformed | Exception
type internal DiagnosticExceptionClass = NoException | AttemptRefusalException | Json | Io | Cancellation | Timeout | InvalidData | Unexpected
type internal ReceiverDiagnostic =
    { RunOrdinal:int;ExecutionRole:string;FirstFailureSite:string;Errno:int option;ExceptionClass:string
      DirectExit:string;Settlement:string;UnknownBeforeFinal:string;StdoutReader:string;StderrReader:string
      FirstRetirement:string;FinalRetirement:string;DeadlineExpired:string }

type internal AttemptDiagnostic =
    { Stage: DiagnosticStage
      Action: DiagnosticAction option
      Outcome: DiagnosticOutcome
      ExitCode: int option
      ReceiverRefusal: string
      ExceptionClass: DiagnosticExceptionClass
      ReceiverDetail: ReceiverDiagnostic option
      RecipeSourceSha: string option
      ProducerSha256: string option
      BindingSha256: string option }

module internal AttemptDiagnostics =
    type private Invocation =
        { mutable RecipeSourceSha: string option
          mutable ProducerSha256: string option
          mutable BindingSha256: string option
          mutable ReceiverDetail: ReceiverDiagnostic option
          mutable First: AttemptDiagnostic option }

    let private current = AsyncLocal<Invocation option>()

    let private receiverRefusals =
        set [
            "arguments-refused"; "binding-drift-refused"; "command-refused"; "effect-admission-refused"
            "git-output-limit-refused"; "git-output-refused"; "git-source-refused"; "git-timeout-refused"
            "input-access-refused"; "input-io-refused"; "input-missing"; "input-size-refused"; "input-symlink-refused"
            "json-array-item-refused"; "json-array-refused"; "json-duplicate-key-refused"; "json-fields-refused"
            "json-integer-refused"; "json-object-refused"; "json-refused"; "json-string-refused"
            "process-baseline-refused"; "process-budget-refused"; "process-cleanup-unknown"; "process-dependency-refused"
            "process-entry-refused"; "process-executable-refused"; "process-pidfd-refused"; "process-platform-refused"
            "process-scope-busy"; "process-scope-refused"; "process-sigchild-refused"; "process-subreaper-refused"
            "profile-native-bytes-refused"; "profile-operation-refused"; "profile-path-refused"
            "profile-runtime-bound-refused"; "profile-schema-refused"; "profile-source-pin-refused"
            "profile-supported-operations-refused"; "run-nonce-refused"; "source-blob-refused"; "source-head-drift"
            "source-pin-content-refused"; "source-pin-digest-refused"; "source-pin-kind-refused"; "source-root-refused"
            "source-sha-refused"; "source-tree-refused"; "source-worktree-dirty"; "unexpected-refused" ]

    let beginInvocation recipeSourceSha producerSha256 bindingSha256 =
        current.Value <- Some { RecipeSourceSha=recipeSourceSha;ProducerSha256=producerSha256;BindingSha256=bindingSha256;ReceiverDetail=None;First=None }

    let setIdentity recipeSourceSha producerSha256 bindingSha256 =
        match current.Value with
        | Some invocation ->
            recipeSourceSha |> Option.iter(fun value->invocation.RecipeSourceSha<-Some value)
            producerSha256 |> Option.iter(fun value->invocation.ProducerSha256<-Some value)
            bindingSha256 |> Option.iter(fun value->invocation.BindingSha256<-Some value)
        | None -> ()

    let private names = set ["schema";"runOrdinal";"executionRole";"firstFailureSite";"errno";"exceptionClass";"directExit";"settlement";"unknownBeforeFinal";"stdoutReader";"stderrReader";"firstRetirement";"finalRetirement";"deadlineExpired"]
    let private parseDetail (json:string) =
        try
            if Encoding.UTF8.GetByteCount(json)>4069 then None else
            use document=JsonDocument.Parse(json,JsonDocumentOptions(MaxDepth=8,CommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false))
            let root=document.RootElement
            if root.ValueKind<>JsonValueKind.Object then None else
            let properties=root.EnumerateObject()|>Seq.toArray
            let actual=properties|>Array.map _.Name
            if actual.Length<>names.Count || Set.ofArray actual<>names then None else
            let text (name:string) (allowed:string->bool) =
                let property=root.GetProperty(name)
                if property.ValueKind=JsonValueKind.String then
                    let value=property.GetString()
                    if allowed value then Some value else None
                else None
            let oneOf values value=values|>Set.contains value
            let observation=set["not-observed";"false";"true"]
            let readers=set["not-observed";"complete";"faulted";"incomplete"]
            let retirements=set["not-observed";"clean";"unknown"]
            let failures=set["none";"child-task-limit";"child-token";"child-enumeration";"identity-read";"identity-parent";"pidfd-open";"identity-recheck";"retained-capacity";"signal";"reap-no-child";"reap-error";"direct-wait";"final-direct-exit";"final-settlement";"prior-unknown";"stdout-reader";"stderr-reader"]
            let classes=set["none";"io";"format";"unauthorized";"invalid-operation";"other"]
            let ordinal=root.GetProperty("runOrdinal")
            let errnoProperty=root.GetProperty("errno")
            let mutable ordinalValue=0
            let mutable errnoValue=0
            let errno = if errnoProperty.ValueKind=JsonValueKind.Null then Some None elif errnoProperty.ValueKind=JsonValueKind.Number && errnoProperty.TryGetInt32(&errnoValue) && errnoValue>=0 && errnoValue<=4096 then Some(Some errnoValue) else None
            match ordinal.ValueKind=JsonValueKind.Number && ordinal.TryGetInt32(&ordinalValue) && ordinalValue>=0 && ordinalValue<=64,
                  text "schema" ((=)"fsgg.telemetry.host-binding-diagnostic/1"),text "executionRole" (oneOf(set["cli";"test"])),text "firstFailureSite" (oneOf failures),errno,
                  text "exceptionClass" (oneOf classes),text "directExit" (oneOf observation),text "settlement" (oneOf observation),text "unknownBeforeFinal" (oneOf observation),
                  text "stdoutReader" (oneOf readers),text "stderrReader" (oneOf readers),text "firstRetirement" (oneOf retirements),text "finalRetirement" (oneOf retirements),text "deadlineExpired" (oneOf observation) with
            | true,Some _,Some role,Some site,Some errno,Some category,Some direct,Some settlement,Some prior,Some stdout,Some stderr,Some first,Some finalResult,Some deadline ->
                Some {RunOrdinal=ordinalValue;ExecutionRole=role;FirstFailureSite=site;Errno=errno;ExceptionClass=category;DirectExit=direct;Settlement=settlement;UnknownBeforeFinal=prior;StdoutReader=stdout;StderrReader=stderr;FirstRetirement=first;FinalRetirement=finalResult;DeadlineExpired=deadline}
            | _ -> None
        with _ -> None

    let receiverRefusal (stderr:string) =
        match current.Value with Some invocation->invocation.ReceiverDetail<-None|None->()
        if String.IsNullOrEmpty stderr then "missing"
        elif Encoding.UTF8.GetByteCount(stderr)>65536 then "unrecognized" else
        let value=if stderr.EndsWith("\n",StringComparison.Ordinal) then stderr.Substring(0,stderr.Length-1) else stderr
        let prefix="host-binding-refused:"
        let diagnosticPrefix="host-binding-diagnostic:"
        let lines=value.Split('\n')
        if lines.Length<1 || lines.Length>2 || lines|>Array.exists(fun line->line.Contains('\r')) || not(lines[0].StartsWith(prefix,StringComparison.Ordinal)) then "unrecognized" else
        let token=lines[0].Substring(prefix.Length)
        if not(receiverRefusals.Contains token) then "unrecognized"
        elif lines.Length=1 then token
        elif not(lines[1].StartsWith(diagnosticPrefix,StringComparison.Ordinal)) then "unrecognized"
        else
            match parseDetail(lines[1].Substring(diagnosticPrefix.Length)) with
            | Some detail ->
                match current.Value with Some invocation->invocation.ReceiverDetail<-Some detail|None->()
                token
            | None -> "unrecognized"

    let exceptionClass (error:exn) =
        match error with
        | :? AttemptRefusal -> AttemptRefusalException
        | :? JsonException -> Json
        | :? OperationCanceledException -> Cancellation
        | :? TimeoutException -> Timeout
        | :? InvalidDataException -> InvalidData
        | :? IOException -> Io
        | _ -> Unexpected

    let action = function
        | FixedAction.ReadPublicIdentity -> ReadPublicIdentity
        | FixedAction.RequestBranchFastForward -> RequestBranchFastForward
        | FixedAction.InspectAuthMetadata -> InspectAuthMetadata
        | FixedAction.InvokeBinding -> InvokeBinding
        | FixedAction.TransferSecret SecretRole.NativeAuth -> TransferNativeAuth
        | FixedAction.TransferSecret SecretRole.EffectAdmission -> TransferEffectAdmission
        | FixedAction.DispatchOnce -> DispatchOnce
        | FixedAction.ListRuns -> ListRuns
        | FixedAction.GetRun _ -> GetRun
        | FixedAction.DownloadRunArtifacts _ -> DownloadRunArtifacts
        | FixedAction.CancelOwnedRun _ -> CancelOwnedRun
        | FixedAction.DeleteSecret SecretRole.NativeAuth -> DeleteNativeAuth
        | FixedAction.DeleteSecret SecretRole.EffectAdmission -> DeleteEffectAdmission
        | FixedAction.ReadSecretAbsence SecretRole.NativeAuth -> ReadNativeAuthAbsence
        | FixedAction.ReadSecretAbsence SecretRole.EffectAdmission -> ReadEffectAdmissionAbsence
        | FixedAction.EmitRootReadback -> EmitRootReadback

    let private stageName = function PreparationRender->"preparation-render"|PreparationDerive->"preparation-derive"|Transport->"transport"|TransportAdapter->"transport-adapter"|Admission->"admission"|EffectCheck->"effect-check"|ActionValidation->"action-validation"
    let private actionName = function ReadPublicIdentity->"read-public-identity"|RequestBranchFastForward->"request-branch-fast-forward"|InspectAuthMetadata->"inspect-auth-metadata"|InvokeBinding->"invoke-binding"|TransferNativeAuth->"transfer-native-auth"|TransferEffectAdmission->"transfer-effect-admission"|DispatchOnce->"dispatch-once"|ListRuns->"list-runs"|GetRun->"get-run"|DownloadRunArtifacts->"download-run-artifacts"|CancelOwnedRun->"cancel-owned-run"|DeleteNativeAuth->"delete-native-auth"|DeleteEffectAdmission->"delete-effect-admission"|ReadNativeAuthAbsence->"read-native-auth-absence"|ReadEffectAdmissionAbsence->"read-effect-admission-absence"|EmitRootReadback->"emit-root-readback"
    let private outcomeName = function Returned->"returned"|ResponseLost->"response-lost"|Malformed->"malformed"|Exception->"exception"
    let private exceptionName = function NoException->"none"|AttemptRefusalException->"attempt-refusal"|Json->"json"|Io->"io"|Cancellation->"cancellation"|Timeout->"timeout"|InvalidData->"invalid-data"|Unexpected->"unexpected"

    let record stage action outcome exitCode receiver exceptionCategory =
        match current.Value with
        | Some invocation when invocation.First.IsNone ->
            invocation.First <- Some {
                Stage=stage;Action=action;Outcome=outcome;ExitCode=exitCode
                ReceiverRefusal=receiver;ExceptionClass=exceptionCategory
                ReceiverDetail=invocation.ReceiverDetail
                RecipeSourceSha=invocation.RecipeSourceSha;ProducerSha256=invocation.ProducerSha256;BindingSha256=invocation.BindingSha256 }
        | _ -> ()

    let snapshot () = current.Value |> Option.bind _.First

    let bytes diagnostic =
        use stream=new MemoryStream()
        use writer=new Utf8JsonWriter(stream)
        let optional (name:string) (value:string option) = match value with Some item->writer.WriteString(name,item)|None->writer.WriteNull(name)
        writer.WriteStartObject()
        writer.WriteString("schema","fsgg.telemetry.host-attempt-diagnostic/1")
        writer.WriteString("stage",stageName diagnostic.Stage)
        optional "action" (diagnostic.Action|>Option.map actionName)
        writer.WriteString("outcome",outcomeName diagnostic.Outcome)
        match diagnostic.ExitCode with Some value->writer.WriteNumber("exitCode",value)|None->writer.WriteNull("exitCode")
        writer.WriteString("receiverRefusal",diagnostic.ReceiverRefusal)
        writer.WriteString("exceptionClass",exceptionName diagnostic.ExceptionClass)
        writer.WritePropertyName("receiverDetail")
        match diagnostic.ReceiverDetail with
        | None -> writer.WriteNullValue()
        | Some detail ->
            writer.WriteStartObject();writer.WriteString("schema","fsgg.telemetry.host-binding-diagnostic/1");writer.WriteNumber("runOrdinal",detail.RunOrdinal);writer.WriteString("executionRole",detail.ExecutionRole);writer.WriteString("firstFailureSite",detail.FirstFailureSite)
            match detail.Errno with Some value->writer.WriteNumber("errno",value)|None->writer.WriteNull("errno")
            writer.WriteString("exceptionClass",detail.ExceptionClass);writer.WriteString("directExit",detail.DirectExit);writer.WriteString("settlement",detail.Settlement);writer.WriteString("unknownBeforeFinal",detail.UnknownBeforeFinal);writer.WriteString("stdoutReader",detail.StdoutReader);writer.WriteString("stderrReader",detail.StderrReader);writer.WriteString("firstRetirement",detail.FirstRetirement);writer.WriteString("finalRetirement",detail.FinalRetirement);writer.WriteString("deadlineExpired",detail.DeadlineExpired);writer.WriteEndObject()
        optional "recipeSourceSha" diagnostic.RecipeSourceSha
        optional "producerSha256" diagnostic.ProducerSha256
        optional "bindingSha256" diagnostic.BindingSha256
        writer.WriteEndObject();writer.Flush()
        stream.ToArray()

    let text diagnostic = Encoding.UTF8.GetString(bytes diagnostic)

    let emitToStderr () =
        try snapshot() |> Option.iter(fun value->Console.Error.WriteLine("host-attempt-diagnostic:"+text value))
        with _ -> ()
