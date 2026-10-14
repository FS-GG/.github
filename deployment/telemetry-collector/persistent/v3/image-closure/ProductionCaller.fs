namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Threading

/// The maintained caller orchestration. Input/CLI custody checks stay with the
/// invoking entry; this operation binds the selected input before any mechanism
/// effect and seals only the actual Runner's accepted terminal state.
module ProductionCaller=
    type Outcome={State:State;Trace:(Effect*Observation) list;QualificationAccepted:bool;EvidenceRoot:string}
    let private execute (inputs:RootlessInputs) target (custody:CustodyChannel) (backend:IProcessBackend) (cancellation:CancellationToken)=
        if target<>"run-c4"&&target<>"run-inactive" then raise(InvalidDataException "caller-target")
        if inputs.CustodyIdentity<>custody.Identity||inputs.Program.Selection.Target<>target then raise(InvalidDataException "caller-input-custody-target")
        custody.BindInput inputs.ExpectedInput
        let mechanism=RootlessMechanism(inputs,backend)
        let deadline=custody.Deadline
        let initial=if target="run-c4" then Runner.initialC4 inputs.ExpectedInput else Runner.initial inputs.ExpectedInput
        let state,trace=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 deadline cancellation mechanism initial
        if Runner.c4Ready state||Runner.qualificationAccepted state then
            mechanism.SealTerminal(state,trace)
            cancellation.ThrowIfCancellationRequested()
            if DateTimeOffset.UtcNow>=deadline then raise(InvalidDataException "terminal-seal-deadline")
        {State=state;Trace=trace;QualificationAccepted=Runner.qualificationAccepted state;EvidenceRoot=inputs.EvidenceRoot}
    let run inputs target custody cancellation=
        execute inputs target custody (RealProcessBackend(custody):>IProcessBackend) cancellation
#if LEARN_SOURCE_COMPOSITION_TEST
    // This entry does not exist in the production assembly. The distinct source
    // test assembly owns inert external/kernel facts and cannot supply admission.
    type SourceBoundary={ReadBuilderIdentity:unit->string;ReadSupervisorIdentity:unit->string;ReadAvailableCapacity:string->int64}
    let runSourceBoundary inputs target custody (boundary:SourceBoundary) cancellation=
        let actual=RealProcessBackend(custody):>IProcessBackend
        let selected={new IProcessBackend with
            member _.ReadBuilderIdentity()=boundary.ReadBuilderIdentity()
            member _.ReadSupervisorIdentity()=boundary.ReadSupervisorIdentity()
            member _.ReadAvailableCapacity path=boundary.ReadAvailableCapacity path
            member _.Start command=actual.Start command
            member _.RetainCommandEvidence bytes=actual.RetainCommandEvidence bytes}
        execute inputs target custody selected cancellation
#endif
