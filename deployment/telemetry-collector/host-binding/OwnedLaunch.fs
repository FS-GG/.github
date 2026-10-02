namespace Fs.Gg.Telemetry.HostBinding

open System
open System.IO
open System.Runtime.InteropServices

module internal OwnedLaunch =
    type Phase = Gated | IdentityRecorded | ReleaseIntended | Released | Retiring | Terminal | Refused
    type State =
        { Phase:Phase;IdentityRecorded:bool;ReleaseIntentions:int;ReleaseMayHaveEffect:bool
          Cancelled:bool;DeadlineExpired:bool;StickyFailure:bool;DirectSettled:bool;DescendantsSettled:bool;ReadersSettled:bool }
    type Action = RecordIdentity | FailIdentity | RequestRelease | AcknowledgeRelease | LoseRelease | Cancel | Expire | BeginRetirement | ObserveSettlement of bool*bool*bool | Finish
    let initial={Phase=Gated;IdentityRecorded=false;ReleaseIntentions=0;ReleaseMayHaveEffect=false;Cancelled=false;DeadlineExpired=false;StickyFailure=false;DirectSettled=false;DescendantsSettled=false;ReadersSettled=false}
    let private refuse state={state with Phase=Refused;StickyFailure=true}
    let apply action state =
        match action with
        | RecordIdentity when state.Phase=Gated && not state.StickyFailure -> {state with Phase=IdentityRecorded;IdentityRecorded=true}
        | FailIdentity when state.Phase=Gated -> refuse state
        | RequestRelease when state.Phase=IdentityRecorded && state.IdentityRecorded && not state.Cancelled && not state.DeadlineExpired && not state.StickyFailure && state.ReleaseIntentions=0 -> {state with Phase=ReleaseIntended;ReleaseIntentions=1;ReleaseMayHaveEffect=true}
        | AcknowledgeRelease when state.Phase=ReleaseIntended && state.ReleaseIntentions=1 -> {state with Phase=Released}
        | LoseRelease when state.ReleaseIntentions=1 -> {state with Phase=Retiring;StickyFailure=true;ReleaseMayHaveEffect=true}
        | Cancel -> {state with Cancelled=true;Phase=(if state.ReleaseMayHaveEffect then Retiring else Refused);StickyFailure=true}
        | Expire -> {state with DeadlineExpired=true;Phase=(if state.ReleaseMayHaveEffect then Retiring else Refused);StickyFailure=true}
        | BeginRetirement when state.Phase=Released || state.Phase=Retiring -> {state with Phase=Retiring}
        | ObserveSettlement(direct,descendants,readers) when state.Phase=Retiring -> {state with DirectSettled=direct;DescendantsSettled=descendants;ReadersSettled=readers}
        | Finish when state.Phase=Retiring && state.DirectSettled && state.DescendantsSettled && state.ReadersSettled && not state.StickyFailure -> {state with Phase=Terminal}
        | _ -> refuse state

    let applyReleaseGuardMutationForCorrespondence removeGuard state =
        if removeGuard then {state with Phase=ReleaseIntended;ReleaseIntentions=state.ReleaseIntentions+1;ReleaseMayHaveEffect=true}
        else apply RequestRelease state

    [<DllImport("libc", SetLastError=true)>]
    extern int private execv(string path, nativeint argv)

    let private executable = function
        | "git" -> "/usr/bin/git"
#if HOST_BINDING_TEST_LAUNCH_ROLES
        | "python3-test" -> "/usr/bin/python3"
        | "sleep-test" -> "/usr/bin/sleep" | "true-test" -> "/usr/bin/true"
#endif
        | _ -> raise(BindingRefusal "process-executable-refused")

    let roleFor path =
        match path with
        | "/usr/bin/git" -> "git"
        | _ -> raise(BindingRefusal "process-executable-refused")

#if HOST_BINDING_TEST_LAUNCH_ROLES
    let roleForTest path =
        match path with
        | "/usr/bin/git" -> "git" | "/usr/bin/python3" -> "python3-test"
        | "/usr/bin/sleep" -> "sleep-test" | "/usr/bin/true" -> "true-test"
        | _ -> raise(BindingRefusal "process-executable-refused")
#endif

    let runLauncher role (arguments:string array) =
        try
            use input=Console.OpenStandardInput()
            if input.ReadByte()<>int(byte 'R') || input.ReadByte() <> -1 then 125 else
            let path=executable role
            let values=Array.append [|path|] arguments
            let pointers=values|>Array.map Marshal.StringToHGlobalAnsi
            let argv=Marshal.AllocHGlobal(IntPtr.Size*(pointers.Length+1))
            try
                pointers|>Array.iteri(fun index value->Marshal.WriteIntPtr(argv,index*IntPtr.Size,value))
                Marshal.WriteIntPtr(argv,pointers.Length*IntPtr.Size,IntPtr.Zero)
                execv(path,argv)|>ignore
                126
            finally
                Marshal.FreeHGlobal(argv)
                pointers|>Array.iter Marshal.FreeHGlobal
        with _ -> 125
