module JoinedCallerTests

open System
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition reason=if not condition then raise(InvalidDataException reason)
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type Effect=delegate of uint32*nativeint*nativeint*nativeint*nativeint*nativeint*nativeint->int
[<DllImport("libc",SetLastError=true)>]
extern int socketpair(int domain,int kind,int protocol,int[] pair)
[<DllImport("libc")>]
extern int close(int fd)
[<DllImport("learn_caller_source",CallingConvention=CallingConvention.Cdecl)>]
extern int fsgg_caller_source_open(nativeint recipe,uint64 deadline,int fd,Effect effect)
[<DllImport("learn_caller_source",CallingConvention=CallingConvention.Cdecl)>]
extern int fsgg_caller_source_serve()
[<DllImport("learn_caller_source",CallingConvention=CallingConvention.Cdecl)>]
extern int fsgg_caller_source_trace(uint32& slots,uint32& births,uint32& signals,int& aborted)
[<DllImport("learn_caller_source",CallingConvention=CallingConvention.Cdecl)>]
extern int fsgg_caller_source_close()
[<DllImport("learn_caller_source",CallingConvention=CallingConvention.Cdecl)>]
extern nativeint fsgg_caller_source_failure()

// Unknown peer retirement must keep the reverse-P/Invoke target alive.
type private CallbackRoot(callback:Effect)=
    let mutable handle=GCHandle.Alloc callback
    member _.Retire(observed:bool)=if observed&&handle.IsAllocated then handle.Free()
    member _.IsRetained=handle.IsAllocated

let callbackRetention()=
    let root,weak=
        let callback=Effect(fun _ _ _ _ _ _ _->17)
        CallbackRoot(callback),WeakReference(callback)
    root.Retire false
    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect()
    require(root.IsRetained&&weak.IsAlive) "source-unknown-peer-callback-not-retained"
    let callback=weak.Target :?> Effect
    require(callback.Invoke(0u,0n,0n,0n,0n,0n,0n)=17) "source-retained-callback-unusable"
    root.Retire true
    require(not root.IsRetained) "source-retired-peer-callback-root-leaked"
    root.Retire true
    GC.KeepAlive callback
    printfn "PASS reverse callback GC root survives unknown peer retirement and releases only after observed retirement"

let private view (value:RootRecipeBinding.NativeView)=
    require(value.Length<=32768un) "source-command-view-bound"
    Marshal.PtrToStringUTF8(value.Pointer,int value.Length)
let private rows<'T> pointer count =Array.init (int count) (fun i->Marshal.PtrToStructure<'T>(pointer+nativeint(i*Marshal.SizeOf<'T>())))
let private command pointer=
    let selected=Marshal.PtrToStructure<RootRecipeBinding.NativeCommand> pointer
    require(selected.ArgumentCount<=256un&&selected.EnvironmentCount<=128un) "source-command-vector-bound"
    {Executable=view selected.Executable;WorkingDirectory=view selected.WorkingDirectory;
     Arguments=rows<RootRecipeBinding.NativeView> selected.Arguments selected.ArgumentCount|>Array.map view;
     Environment=rows<RootRecipeBinding.NativeEnvironment> selected.Environment selected.EnvironmentCount|>Array.map(fun row->view row.Name,view row.Value)|>Map.ofArray;OutputLimit=int selected.OutputLimit}

let private exercise target fault=
    let failFirst=fault="failed-build"
    let mismatch=fault="input-drift"||fault="command-drift"
    CallerSourceFixtures.withContractInputs(fun selection trusted->
      OciTests.withInactiveInputs selection trusted target (fun inputs runEffect (actual:ResizeArray<ProcessCommand>) (removals:Collections.Generic.HashSet<string>)->
        let original=CallerCompositionTests.recipeCorrespondence inputs
        let recipe=
            if fault="command-drift" then
                let steps=Array.copy original.Steps
                let selected=steps[0]
                steps[0]<-{selected with Command={selected.Command with Arguments=Array.append selected.Command.Arguments [|"--inert-drift"|]}}
                {original with Steps=steps}
            else original
        let allocations=Collections.Generic.List<nativeint>()
        let mutable callbackFailure:exn option=None
        let output pointer lengthPointer text=
            let bytes=Encoding.UTF8.GetBytes(text:string)
            let memory=Marshal.AllocHGlobal(max 1 bytes.Length)
            allocations.Add memory
            Marshal.Copy(bytes,0,memory,bytes.Length)
            Marshal.WriteIntPtr(pointer,memory);Marshal.WriteIntPtr(lengthPointer,nativeint bytes.Length)
        let callback=Effect(fun slot selected exitCode stdout stdoutCount stderr stderrCount->
            try
                let selected=command selected
                let result=if failFirst&&slot=0u then {ExitCode=42;Stdout="";Stderr="inert build refusal"} else runEffect selected
                Marshal.WriteInt32(exitCode,result.ExitCode)
                output stdout stdoutCount result.Stdout;output stderr stderrCount result.Stderr
                1
            with error->callbackFailure<-Some error;0)
        // An unretired native peer keeps its callback rooted even after refusal.
        // Unknown cleanup retains the handle and unmanaged output allocations.
        let callbackRoot=CallbackRoot(callback)
        let pair=[|-1;-1|]
        let deadline=DateTimeOffset.UtcNow.AddSeconds 90.
        let mutable opened=false
        let mutable peer:Task<int> option=None
        let oldFd=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_FD"
        let oldBinding=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_BINDING"
        try
            require(socketpair(1,1|||0o2000000,0,pair)=0) "source-socketpair"
            RootRecipeBinding.withNative recipe (fun native->
                require(fsgg_caller_source_open(native,uint64(deadline.ToUnixTimeMilliseconds()),pair[1],callback)=1) "source-native-open"
                opened<-true)
            close pair[1]|>ignore;pair[1]<- -1
            peer<-Some(Task.Factory.StartNew((fun()->fsgg_caller_source_serve()),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default))
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",string pair[0])
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes {|schema=CustodyProtocol.Schema;identity=inputs.CustodyIdentity;deadline=deadline.ToString("O");admissionSha256=inputs.CustodyIdentity|}))
            let mutable callerError:exn option=None
            let outcome=
              try
                use custody=CustodyChannel.BindInherited()
                pair[0]<- -1
                let boundary:ProductionCaller.SourceBoundary={ReadBuilderIdentity=(fun()->RootlessPolicy.BuilderSha256);ReadSupervisorIdentity=(fun()->RootlessPolicy.SupervisorSha256);ReadAvailableCapacity=(fun _->128L*1024L*1024L*1024L)}
                let callerInputs=if fault="input-drift" then {inputs with ExpectedInput=String('e',64)} else inputs
                Some(ProductionCaller.runSourceBoundary callerInputs target custody boundary CancellationToken.None)
              with error when mismatch->callerError<-Some error;None
            require(peer.Value.Wait(TimeSpan.FromSeconds 5.)) "source-peer-not-retired"
            callbackFailure|>Option.iter raise
            let mutable slots,births,signals,aborted=0u,0u,0u,0
            let terminal=fsgg_caller_source_trace(&slots,&births,&signals,&aborted)
            if mismatch then
                require(peer.Value.Result=0&&terminal=0&&births=0u&&signals=0u&&actual.Count=0) "source-drift-born-or-accepted"
                require(slots=(if fault="input-drift" then 0u else 1u)) "source-drift-slot-consumption"
                match outcome with
                | Some value->require(not(Runner.c4Ready value.State)&&not value.QualificationAccepted&&value.State.CleanupFailed) "source-drift-cleanup-accepted"
                | None->require(callerError.IsSome) "source-drift-refusal-lost"
                require(not(String.IsNullOrWhiteSpace(Marshal.PtrToStringUTF8(fsgg_caller_source_failure())))) "source-drift-cause-lost"
            else
                require(peer.Value.Result=1) ("source-native-terminal-refused:"+Marshal.PtrToStringUTF8(fsgg_caller_source_failure()))
                require(terminal=1&&slots=births&&signals=0u) "source-native-retirement-trace"
                let outcome=outcome.Value
                if failFirst then
                    require(not(Runner.c4Ready outcome.State)&&not outcome.QualificationAccepted&&aborted=1) "source-failed-build-accepted"
                else
                    require(aborted=0&&actual.Count=recipe.NormalCount+16&&removals.Count=4) "source-command-stream-count"
                    require((if target="run-c4" then Runner.c4Ready outcome.State else outcome.QualificationAccepted)) "source-caller-outcome"
                let bundleAbsent name=not(Directory.Exists(Path.Combine(inputs.WorkRoot,name)))
                require(bundleAbsent "bundle-a"&&bundleAbsent "bundle-b") "source-bundles-retained"
            printfn "PASS joined caller/C ABI/wire/finite policy/capture/cleanup target=%s fault=%s slots=%d births=%d; native acceptance NOT_RUN" target fault slots births
        finally
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",oldFd)
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",oldBinding)
            for fd in pair do if fd>=0 then close fd|>ignore
            let retired=match peer with None->true|Some task->task.Wait(TimeSpan.FromSeconds 5.)
            if opened&&retired then require(fsgg_caller_source_close()=1) "source-native-close"
            if retired then
                for memory in allocations do Marshal.FreeHGlobal memory
            callbackRoot.Retire retired
            GC.KeepAlive callback
            require retired "source-peer-cleanup-unknown"))

let run()=
    callbackRetention()
    exercise "run-c4" "none"
    exercise "run-inactive" "none"
    exercise "run-c4" "failed-build"
    exercise "run-c4" "input-drift"
    exercise "run-c4" "command-drift"
