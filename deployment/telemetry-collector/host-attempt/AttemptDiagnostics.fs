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
    { ReceiverSchema:string;RunOrdinal:int;ExecutionRole:string;FirstFailureSite:string;Errno:int option;ExceptionClass:string
      DirectExit:string;Settlement:string;UnknownBeforeFinal:string;StdoutReader:string;StderrReader:string
      FirstRetirement:string;FinalRetirement:string;DeadlineExpired:string;ReadOrigin:string option
      ManagedException:string option;ReadGuard:string option;AcquisitionPass:string option;CandidateRelation:string option
      FirstFailureRunOrdinal:int option;ScopeFirstRetirementRunOrdinal:int option;RunFirstRetirement:string option;RunFinalRetirement:string option;RetirementRunOrdinal:int option }

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
    let private namesV2 = set ["schema";"runOrdinal";"executionRole";"firstFailureSite";"errno";"exceptionClass";"directExit";"settlement";"unknownBeforeFinal";"stdoutReader";"stderrReader";"scopeFirstRetirement";"scopeFinalRetirement";"deadlineExpired";"readOrigin";"managedException";"readGuard";"acquisitionPass";"candidateRelation";"firstFailureRunOrdinal";"scopeFirstRetirementRunOrdinal";"runFirstRetirement";"runFinalRetirement";"retirementRunOrdinal"]
    let private parseDetail (json:string) =
        try
            if Encoding.UTF8.GetByteCount(json)>4069 then None else
            use document=JsonDocument.Parse(json,JsonDocumentOptions(MaxDepth=8,CommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false))
            let root=document.RootElement
            if root.ValueKind<>JsonValueKind.Object then None else
            let properties=root.EnumerateObject()|>Seq.toArray
            let actual=properties|>Array.map _.Name
            let actualNames=Set.ofArray actual
            if actual.Length<>actualNames.Count || (actualNames<>names && actualNames<>namesV2) then None else
            let text (name:string) (allowed:string->bool) =
                let property=root.GetProperty(name)
                if property.ValueKind=JsonValueKind.String then let value=property.GetString() in if allowed value then Some value else None
                else None
            let oneOf values value=values|>Set.contains value
            let ordinalField (name:string) =
                let property=root.GetProperty(name)
                let mutable value=0
                if property.ValueKind=JsonValueKind.Number && property.TryGetInt32(&value) && value>=0 && value<=64 then Some value else None
            let nullableErrno () =
                let property=root.GetProperty("errno")
                let mutable value=0
                if property.ValueKind=JsonValueKind.Null then Some None
                elif property.ValueKind=JsonValueKind.Number && property.TryGetInt32(&value) && value>=0 && value<=4096 then Some(Some value)
                else None
            let observation=set["not-observed";"false";"true"]
            let readers=set["not-observed";"complete";"faulted";"incomplete"]
            let retirements=set["not-observed";"clean";"unknown"]
            let failures=set["none";"child-task-limit";"child-token";"child-enumeration";"identity-read";"identity-parent";"pidfd-open";"identity-recheck";"retained-capacity";"signal";"reap-no-child";"reap-error";"direct-wait";"final-direct-exit";"final-settlement";"prior-unknown";"stdout-reader";"stderr-reader"]
            let classes=set["none";"io";"format";"unauthorized";"invalid-operation";"other"]
            match ordinalField "runOrdinal",text "executionRole" (oneOf(set["cli";"test"])),text "firstFailureSite" (oneOf failures),nullableErrno(),text "exceptionClass" (oneOf classes),text "directExit" (oneOf observation),text "settlement" (oneOf observation),text "unknownBeforeFinal" (oneOf observation),text "stdoutReader" (oneOf readers),text "stderrReader" (oneOf readers),text "deadlineExpired" (oneOf observation) with
            | Some ordinal,Some role,Some site,Some errno,Some category,Some direct,Some settlement,Some prior,Some stdout,Some stderr,Some deadline when actualNames=names ->
                match text "schema" ((=)"fsgg.telemetry.host-binding-diagnostic/1"),text "firstRetirement" (oneOf retirements),text "finalRetirement" (oneOf retirements) with
                | Some schemaValue,Some first,Some finalResult -> Some {ReceiverSchema=schemaValue;RunOrdinal=ordinal;ExecutionRole=role;FirstFailureSite=site;Errno=errno;ExceptionClass=category;DirectExit=direct;Settlement=settlement;UnknownBeforeFinal=prior;StdoutReader=stdout;StderrReader=stderr;FirstRetirement=first;FinalRetirement=finalResult;DeadlineExpired=deadline;ReadOrigin=None;ManagedException=None;ReadGuard=None;AcquisitionPass=None;CandidateRelation=None;FirstFailureRunOrdinal=None;ScopeFirstRetirementRunOrdinal=None;RunFirstRetirement=None;RunFinalRetirement=None;RetirementRunOrdinal=None}
                | _ -> None
            | Some ordinal,Some role,Some site,Some errno,Some category,Some direct,Some settlement,Some prior,Some stdout,Some stderr,Some deadline when actualNames=namesV2 ->
                let origins=set["not-observed";"existence-guard";"metadata-length-guard";"content-read";"post-read-byte-guard";"stat-parse"]
                let managed=set["not-observed";"file-not-found";"directory-not-found";"unauthorized";"generic-io";"format";"other";"none"]
                let guards=set["not-observed";"exists-false";"length-negative";"length-over";"byte-length-over";"none"]
                match text "schema" ((=)"fsgg.telemetry.host-binding-diagnostic/2"),text "scopeFirstRetirement" (oneOf retirements),text "scopeFinalRetirement" (oneOf retirements),text "runFirstRetirement" (oneOf retirements),text "runFinalRetirement" (oneOf retirements),text "readOrigin" (oneOf origins),text "managedException" (oneOf managed),text "readGuard" (oneOf guards),text "acquisitionPass" (oneOf(set["not-observed";"initial";"recheck"])),text "candidateRelation" (oneOf(set["not-observed";"active-direct";"other"])),ordinalField "firstFailureRunOrdinal",ordinalField "scopeFirstRetirementRunOrdinal",ordinalField "retirementRunOrdinal" with
                | Some schemaValue,Some scopeFirst,Some scopeFinal,Some runFirst,Some runFinal,Some origin,Some managedValue,Some guard,Some pass,Some relation,Some failureOrdinal,Some scopeOrdinal,Some retirementOrdinal -> Some {ReceiverSchema=schemaValue;RunOrdinal=ordinal;ExecutionRole=role;FirstFailureSite=site;Errno=errno;ExceptionClass=category;DirectExit=direct;Settlement=settlement;UnknownBeforeFinal=prior;StdoutReader=stdout;StderrReader=stderr;FirstRetirement=scopeFirst;FinalRetirement=scopeFinal;DeadlineExpired=deadline;ReadOrigin=Some origin;ManagedException=Some managedValue;ReadGuard=Some guard;AcquisitionPass=Some pass;CandidateRelation=Some relation;FirstFailureRunOrdinal=Some failureOrdinal;ScopeFirstRetirementRunOrdinal=Some scopeOrdinal;RunFirstRetirement=Some runFirst;RunFinalRetirement=Some runFinal;RetirementRunOrdinal=Some retirementOrdinal}
                | _ -> None
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
            writer.WriteStartObject();writer.WriteString("schema",detail.ReceiverSchema);writer.WriteNumber("runOrdinal",detail.RunOrdinal);writer.WriteString("executionRole",detail.ExecutionRole);writer.WriteString("firstFailureSite",detail.FirstFailureSite)
            match detail.Errno with Some value->writer.WriteNumber("errno",value)|None->writer.WriteNull("errno")
            writer.WriteString("exceptionClass",detail.ExceptionClass);writer.WriteString("directExit",detail.DirectExit);writer.WriteString("settlement",detail.Settlement);writer.WriteString("unknownBeforeFinal",detail.UnknownBeforeFinal);writer.WriteString("stdoutReader",detail.StdoutReader);writer.WriteString("stderrReader",detail.StderrReader)
            if detail.ReceiverSchema.EndsWith("/1",StringComparison.Ordinal) then writer.WriteString("firstRetirement",detail.FirstRetirement);writer.WriteString("finalRetirement",detail.FinalRetirement)
            else
                writer.WriteString("scopeFirstRetirement",detail.FirstRetirement);writer.WriteString("scopeFinalRetirement",detail.FinalRetirement)
                optional "readOrigin" detail.ReadOrigin;optional "managedException" detail.ManagedException;optional "readGuard" detail.ReadGuard;optional "acquisitionPass" detail.AcquisitionPass;optional "candidateRelation" detail.CandidateRelation
                match detail.FirstFailureRunOrdinal with Some value->writer.WriteNumber("firstFailureRunOrdinal",value)|None->writer.WriteNull("firstFailureRunOrdinal")
                match detail.ScopeFirstRetirementRunOrdinal with Some value->writer.WriteNumber("scopeFirstRetirementRunOrdinal",value)|None->writer.WriteNull("scopeFirstRetirementRunOrdinal")
                optional "runFirstRetirement" detail.RunFirstRetirement;optional "runFinalRetirement" detail.RunFinalRetirement
                match detail.RetirementRunOrdinal with Some value->writer.WriteNumber("retirementRunOrdinal",value)|None->writer.WriteNull("retirementRunOrdinal")
            writer.WriteString("deadlineExpired",detail.DeadlineExpired);writer.WriteEndObject()
        optional "recipeSourceSha" diagnostic.RecipeSourceSha
        optional "producerSha256" diagnostic.ProducerSha256
        optional "bindingSha256" diagnostic.BindingSha256
        writer.WriteEndObject();writer.Flush()
        stream.ToArray()

    let text diagnostic = Encoding.UTF8.GetString(bytes diagnostic)

    let emitToStderr () =
        try snapshot() |> Option.iter(fun value->Console.Error.WriteLine("host-attempt-diagnostic:"+text value))
        with _ -> ()
