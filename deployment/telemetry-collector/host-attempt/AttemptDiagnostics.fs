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

type internal AttemptDiagnostic =
    { Stage: DiagnosticStage
      Action: DiagnosticAction option
      Outcome: DiagnosticOutcome
      ExitCode: int option
      ReceiverRefusal: string
      ExceptionClass: DiagnosticExceptionClass
      RecipeSourceSha: string option
      ProducerSha256: string option
      BindingSha256: string option }

module internal AttemptDiagnostics =
    type private Invocation =
        { mutable RecipeSourceSha: string option
          mutable ProducerSha256: string option
          mutable BindingSha256: string option
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
        current.Value <- Some { RecipeSourceSha=recipeSourceSha;ProducerSha256=producerSha256;BindingSha256=bindingSha256;First=None }

    let setIdentity recipeSourceSha producerSha256 bindingSha256 =
        match current.Value with
        | Some invocation ->
            recipeSourceSha |> Option.iter(fun value->invocation.RecipeSourceSha<-Some value)
            producerSha256 |> Option.iter(fun value->invocation.ProducerSha256<-Some value)
            bindingSha256 |> Option.iter(fun value->invocation.BindingSha256<-Some value)
        | None -> ()

    let receiverRefusal (stderr:string) =
        if String.IsNullOrEmpty stderr then "missing" else
        let value=stderr.TrimEnd('\r','\n')
        let prefix="host-binding-refused:"
        if value.StartsWith(prefix,StringComparison.Ordinal) && value.IndexOfAny([|'\r';'\n'|])<0 then
            let token=value.Substring(prefix.Length)
            if receiverRefusals.Contains token then token else "unrecognized"
        else "unrecognized"

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
        optional "recipeSourceSha" diagnostic.RecipeSourceSha
        optional "producerSha256" diagnostic.ProducerSha256
        optional "bindingSha256" diagnostic.BindingSha256
        writer.WriteEndObject();writer.Flush()
        stream.ToArray()

    let text diagnostic = Encoding.UTF8.GetString(bytes diagnostic)

    let emitToStderr () =
        try snapshot() |> Option.iter(fun value->Console.Error.WriteLine("host-attempt-diagnostic:"+text value))
        with _ -> ()
