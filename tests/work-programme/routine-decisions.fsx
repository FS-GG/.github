#if CLAUDE_SKILLS
#load "../../.claude/skills/work-programme/scripts/programme.fsx"
#else
#load "../../.agents/skills/work-programme/scripts/programme.fsx"
#endif
open System
open System.IO
open System.Text
open System.Text.Json
open Programme

let now=DateTimeOffset.Parse "2026-10-13T20:05:03.741Z"
let hash=String('a',64)
let lane : Lane = {
    Id="A";Feature="FEATURE";Item="A";OriginalItem="A";Attempt="attempt";Repository="FS-GG/Example"
    Chain="A";Owner="owner";Worktree="/synthetic/worktree";Head="candidate";Priority=1;Readable=true
    ObservedAt=now;Evidence=[|"synthetic-native-reference"|];State="repair";InFlight=true
    TouchSet=[|"src/A/**"|];Dependencies=[||];Source="local";Validation="pending"
    Publication="not-required";Installed="not-required";Native="not-required";Projection="not-required" }
let snapshot : Snapshot={Schema="fsgg.programme.snapshot/1";Campaign="campaign";Capacity=1;CapacityKnown=true;MaxAgeSeconds=300;Lanes=[|lane|];OpenPulls=[||]}
let delta : DeltaInput={
    Schema="fsgg.programme.delta-input/1";EvaluationTime=now;EvaluatorIdentity="routine-1";PolicyIdentity="policy-1";UserScopeRevision="scope-1"
    BaseRevision="v1";CurrentRevision="v2";BaseEvaluatorIdentity="routine-1";BasePolicyIdentity="policy-1";BaseUserScopeRevision="scope-1"
    BaseReturns=[||];CurrentReturns=[||];Snapshot=snapshot }
let operation : RoutineOperation={
    Lane="A";Operation="original-operation";OriginalAttempt="original-attempt";Attempt="attempt";Owner="owner";Candidate="candidate"
    Revision=1L;Supersedes=0L;Event="failed";Class="ordinary";State="repair-needed";ObservedAt=now;Cleanup="observed"
    ExecutionDeadline=now.AddMinutes(5.);ExecutionMode="enforced";ObservationDeadline=now.AddMinutes(10.)
    Cpu=2;MemoryMiB=4096;AllocationCpu=2;AllocationMemoryMiB=4096;AllocationMode="soft"
    Evidence="synthetic-first-failure";Sha256=hash;History="synthetic-original-log" }
let resources : RoutineResources={Known=true;ObservedAt=now;Cpu=12;MemoryMiB=32768;OccupiedCpu=10;OccupiedMemoryMiB=28672;Evidence="synthetic-resource-read";AgentMode="soft";CpuMemoryMode="soft"}
let spec : RoutineInput={Schema="fsgg.programme.routine-decisions-input/1";Delta=delta;Resources=resources;Events=[|operation|];Previous=[||];PreviousPolicyIdentity="";PreviousEvaluatorIdentity="";PreviousUserScopeRevision=""}
let evaluate input=routineDecisions hash input
let decision input=(evaluate input).Current[0]
let expect condition=if not condition then failwith "unexpected result"
let action code input=expect ((decision input).Action=code)
let refuses f=expect (try f();false with _ -> true)
let has reason input=Array.contains reason (decision input).Reasons
let change o={spec with Events=[|o|]}
let previous result={spec with Previous=result.Current;PreviousPolicyIdentity=delta.PolicyIdentity;PreviousEvaluatorIdentity=delta.EvaluatorIdentity;PreviousUserScopeRevision=delta.UserScopeRevision}
let mutable passed=0
let test name f=f();passed<-passed+1;printfn "PASS %s" name

test "pure explicit time replay" (fun () -> expect (encode(evaluate spec)=encode(evaluate spec)))
test "ordinary cleanup repair at full agent capacity" (fun () -> action "return-existing-owner" spec;expect (has "ordinary-repair" spec && (evaluate spec).ActiveReservations=[|lane|]))
test "cleanup absent never permits retry" (fun () -> action "observe-original-identity" (change {operation with Cleanup="unconfirmed"}))
test "expiry and cancel are not cleanup" (fun () -> action "observe-original-identity" (change {operation with Cleanup="unconfirmed";ExecutionDeadline=now.AddSeconds(-1.)}))
test "unknown original effect precedes retry" (fun () -> action "observe-original-identity" (change {operation with Event="unknown-effect";State="unknown-effect"}))
test "unknown operation is observation only" (fun () -> action "observe-original-identity" (change {operation with Class="unknown"}))
test "protected clean terminal retains admission" (fun () -> action "retain-admission" (change {operation with Class="protected";State="accepted"}))
test "protected event retains admission" (fun () -> action "retain-admission" (change {operation with Event="admission-required"}))
test "enforced original deadline never renewed" (fun () -> action "retain-admission" (change {operation with ExecutionDeadline=now}))
test "soft elapsed target distinguished" (fun () -> let input=change {operation with ExecutionDeadline=now;ExecutionMode="soft"} in action "return-existing-owner" input;expect (has "soft-target-exceeded" input))
test "observation budget separate from execution" (fun () -> action "return-existing-owner" (change {operation with Class="observation";ExecutionDeadline=now.AddSeconds(-1.);State="running"}))
test "expired read-only observation refreshes original" (fun () -> action "refresh-facts" (change {operation with Class="observation";ObservationDeadline=now;State="running"}))
test "unknown deadline fences ordinary execution" (fun () -> action "retain-admission" (change {operation with ExecutionMode="unknown";ExecutionDeadline=DateTimeOffset.MinValue}))
test "resource occupancy fences cpu" (fun () -> action "wait-for-resources" {spec with Resources={resources with OccupiedCpu=11}})
test "resource occupancy fences memory" (fun () -> action "wait-for-resources" {spec with Resources={resources with OccupiedMemoryMiB=30000}})
test "soft allocation target is not a hard cap" (fun () -> let input=change {operation with AllocationCpu=1} in action "return-existing-owner" input;expect (has "soft-target-exceeded" input))
test "enforced allocation cap blocks" (fun () -> action "wait-for-resources" (change {operation with AllocationCpu=1;AllocationMode="enforced"}))
test "unknown resource proof blocks" (fun () -> action "wait-for-resources" {spec with Resources={resources with Known=false}})
test "stale resource proof blocks" (fun () -> action "wait-for-resources" {spec with Resources={resources with ObservedAt=now.AddMinutes(-6.)}})
test "unchanged semantic current emits no change" (fun () -> expect ((evaluate(previous(evaluate spec))).Changed.Length=0))
test "same event duplicates idempotent" (fun () -> expect (encode(evaluate {spec with Events=[|operation;operation|]})=encode(evaluate spec)))
test "out of order independent supplied revisions" (fun () -> let next={operation with Revision=2L;Supersedes=1L;Event="cleanup-observed";State="cleanup-observed"} in expect (encode(evaluate {spec with Events=[|operation;next|]})=encode(evaluate {spec with Events=[|next;operation|]})))
test "equal revision conflict fences" (fun () -> action "escalate-conflict-or-scope" {spec with Events=[|operation;{operation with Evidence="different"}|]})
test "newer event cannot erase lower revision conflict" (fun () -> action "escalate-conflict-or-scope" {spec with Events=[|operation;{operation with Evidence="different"};{operation with Revision=2L;Supersedes=1L}|]})
test "conflict carried in sole current view" (fun () -> let result=evaluate {spec with Events=[|operation;{operation with Evidence="different"}|]} in action "escalate-conflict-or-scope" {previous result with Events=[|{operation with Revision=2L;Supersedes=1L}|]})
test "unknown effect sticky through clean summary" (fun () -> let result=evaluate(change {operation with Event="unknown-effect";State="unknown-effect"}) in action "observe-original-identity" {previous result with Events=[|{operation with Revision=2L;Supersedes=1L;State="accepted"}|]})
test "fresh original effect reconciliation permits next bounded state" (fun () -> let result=evaluate(change {operation with Event="unknown-effect";State="unknown-effect"}) in action "return-existing-owner" {previous result with Events=[|{operation with Revision=2L;Supersedes=1L;Event="effect-reconciled";State="cleanup-observed"}|]})
test "original attempt conflict retained" (fun () -> action "escalate-conflict-or-scope" {spec with Events=[|operation;{operation with OriginalAttempt="different"}|]})
test "missing initial observation revision refreshes" (fun () -> action "refresh-facts" (change {operation with Revision=3L;Supersedes=2L}))
test "missing subsequent observation revision refreshes" (fun () -> action "refresh-facts" {previous(evaluate spec) with Events=[|{operation with Revision=3L;Supersedes=2L}|]})
test "stale event refreshes without time renewal" (fun () -> action "refresh-facts" (change {operation with ObservedAt=now.AddMinutes(-6.)}))
test "future event refreshes" (fun () -> action "refresh-facts" (change {operation with ObservedAt=now.AddSeconds(1.)}))
test "missing base resynchronizes but reserves" (fun () -> let input={spec with Delta={delta with BaseRevision=""}} in action "refresh-facts" input;expect ((evaluate input).ActiveReservations=[|lane|]))
test "policy scope evaluator changes fence continuation" (fun () -> for input in [{previous(evaluate spec) with PreviousPolicyIdentity="other"};{previous(evaluate spec) with PreviousEvaluatorIdentity="other"};{previous(evaluate spec) with PreviousUserScopeRevision="other"}] do action "escalate-conflict-or-scope" input)
test "shared protected surface overlap escalates" (fun () -> let other={lane with Id="B";Item="B";OriginalItem="B";Chain="B"} in action "escalate-conflict-or-scope" {spec with Delta={delta with Snapshot={snapshot with Lanes=[|lane;other|]}}})
test "foreign candidate input refuses" (fun () -> refuses (fun () -> evaluate(change {operation with Candidate="different"}) |> ignore))
test "missing lane cannot drop previous reservation" (fun () -> refuses (fun () -> evaluate {previous(evaluate spec) with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with Id="B"}|]}};Events=[||]} |> ignore))
test "terminal cleanup removes stale running state" (fun () -> let input={change {operation with State="accepted";Event="current-observation"} with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="closed";Validation="passed"}|]}}} in expect ((decision input).State="accepted"))
test "integration routes existing integrator with external admission" (fun () -> let input=change {operation with Class="integration"} in action "return-existing-owner" input;expect (has "integration-admission-external" input))
test "no new worker implicit at full capacity" (fun () -> action "retain-admission" {spec with Events=[||];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with InFlight=false;State="active"}|]}}})
test "protected class cannot downgrade on later revision" (fun () ->
    let result=evaluate(change {operation with Class="protected"})
    let input={previous result with Events=[|{operation with Revision=2L;Supersedes=1L}|]}
    action "retain-admission" input
    expect ((decision input).Operations[0].Class="protected" && has "revision-conflict" input))
test "expired deadline cannot move forward" (fun () ->
    let expired={operation with ExecutionDeadline=now}
    let input={previous(evaluate(change expired)) with Events=[|{operation with Revision=2L;Supersedes=1L}|]}
    action "escalate-conflict-or-scope" input
    expect ((decision input).Operations[0].ExecutionDeadline=now))
test "allocation cannot relax enforced original bounds" (fun () ->
    let strict={operation with AllocationMode="enforced"}
    action "escalate-conflict-or-scope" {previous(evaluate(change strict)) with Events=[|{operation with Revision=2L;Supersedes=1L}|]})
test "unknown reservation survives false snapshot and fences overlap" (fun () ->
    let unknown={operation with Event="unknown-effect";State="unknown-effect"}
    let other={lane with Id="B";Item="B";OriginalItem="B";Chain="B";InFlight=false;State="active"}
    let input={previous(evaluate(change unknown)) with Events=[||];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with InFlight=false};other|]}}}
    let result=evaluate input
    expect (result.ActiveReservations |> Array.exists (fun l -> l.Id="A"))
    expect (result.Current |> Array.exists (fun d -> d.Lane="B" && Array.contains "shared-surface-conflict" d.Reasons)))
test "unclean reservation survives false snapshot" (fun () ->
    let dirty={operation with Cleanup="unconfirmed"}
    let input={previous(evaluate(change dirty)) with Events=[||];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with InFlight=false}|]}}}
    expect ((evaluate input).ActiveReservations.Length=1)
    action "observe-original-identity" input)
test "failed running label cannot bypass cleanup" (fun () -> action "observe-original-identity" (change {operation with State="running";Cleanup="unconfirmed"}))
test "repair without observed operation cannot retry" (fun () -> action "observe-original-identity" {spec with Events=[||]})
test "scope event remains unresolved on later ordinary observation" (fun () ->
    let result=evaluate(change {operation with Event="scope-changed"})
    let input={previous result with Events=[|{operation with Revision=2L;Supersedes=1L;Event="current-observation"}|]}
    action "escalate-conflict-or-scope" input
    expect (Array.contains "scope-policy-evaluator-changed" (decision input).Unresolved))
test "accepted label cannot renew expired operation for repair" (fun () -> action "retain-admission" (change {operation with State="accepted";ExecutionDeadline=now}))
test "complete supplied first revision chain admits ordinary repair" (fun () ->
    action "return-existing-owner" {spec with Events=[|operation;{operation with Revision=2L;Supersedes=1L}|]})
test "source dependency uses its declared accepted boundary" (fun () ->
    let other={lane with Id="B";Item="B";OriginalItem="B";Chain="B";Source="merged";State="active";InFlight=false;TouchSet=[|"src/B/**"|]}
    let input={spec with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with Dependencies=[|{Lane="B";Boundary="source"}|]};other|]}}}
    action "return-existing-owner" input)
test "global delta view revision conflict cannot be ignored" (fun () ->
    let row : LaneReturn={
        Schema="fsgg.programme.lane-return/1";Campaign="campaign";Lane="A";Feature="FEATURE";Item="A";OriginalItem="A"
        OriginalAttempt="original-attempt";Attempt="attempt";Candidate="candidate";Owner="owner"
        Revision=1L;Supersedes=0L;SourceRevision="candidate";InputPacketSha256=hash;ObservedAt=now
        Outcome="window-reported";Boundaries=returnBoundaries lane;Evidence=[|{Kind="mechanical";Reference="synthetic";Sha256=hash;Scope="synthetic"}|]
        Unknowns=[||];Exception="none";Continuation="retain owner";Narrative="synthetic"}
    action "refresh-facts" {spec with Delta={delta with CurrentRevision=delta.BaseRevision;CurrentReturns=[|row|]}})
test "changed current owner cannot discard original operation" (fun () ->
    let input={previous(evaluate spec) with Events=[||];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with Owner="new-owner"}|]}}}
    action "escalate-conflict-or-scope" input
    expect ((decision input).Operations[0].Owner="owner"))
test "unknown accepted deadline cannot authorize repair" (fun () ->
    action "retain-admission" (change {operation with State="accepted";ExecutionDeadline=DateTimeOffset.MinValue;ExecutionMode="unknown"}))
test "first failure history pointer cannot be replaced" (fun () ->
    let input={previous(evaluate spec) with Events=[|{operation with Revision=2L;Supersedes=1L;History="replacement"}|]}
    action "escalate-conflict-or-scope" input
    expect ((decision input).Operations[0].History=operation.History))
test "unknown class cannot release reservation by downgrade" (fun () ->
    let unknown={operation with Class="unknown"}
    let input={previous(evaluate(change unknown)) with Events=[|{operation with Revision=2L;Supersedes=1L}|];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with InFlight=false}|]}}}
    expect ((evaluate input).ActiveReservations.Length=1)
    action "observe-original-identity" input)
test "table multi-cycle unresolved obligations retain holds and fence overlap" (fun () ->
    for tag,expected,obligation in ["unknown-effect","observe-original-identity","unknown-effect";
                                    "admission-required","retain-admission","protected-admission";
                                    "scope-changed","escalate-conflict-or-scope","scope-policy-evaluator-changed"] do
        let mutable current=evaluate(change {operation with Event=tag})
        for revision in 2L..5L do
            let clean={operation with Revision=revision;Supersedes=revision-1L;Event="current-observation";State="accepted"}
            let other={lane with Id="B";Item="B";OriginalItem="B";Chain="B";State="active";InFlight=false}
            let otherOperation={operation with Lane="B";Operation="other-operation"}
            let input={previous current with Events=[|clean;otherOperation|];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active";InFlight=false};other|]}}}
            current<-evaluate input
            let a=current.Current |> Array.find (fun d -> d.Lane="A")
            expect (a.Action=expected && Array.contains obligation a.Unresolved)
            expect (current.ActiveReservations |> Array.exists (fun l -> l.Id="A"))
            expect (current.Current |> Array.exists (fun d -> d.Lane="B" && Array.contains "shared-surface-conflict" d.Reasons)))
test "fresh original effect reconciliation releases prior obligation with clean joins" (fun () ->
    let first=evaluate(change {operation with Event="unknown-effect"})
    let clean={operation with Revision=2L;Supersedes=1L;Event="current-observation";State="accepted"}
    let second=evaluate {previous first with Events=[|clean|]}
    let resolved={operation with Revision=3L;Supersedes=2L;Event="effect-reconciled";State="accepted"}
    let third=evaluate {previous second with Events=[|resolved|];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="closed";Validation="passed";InFlight=false}|]}}}
    expect (third.Current[0].Unresolved.Length=0 && third.ActiveReservations.Length=0 && third.Current[0].State="accepted"))
test "cleanup gap stale future or conflicted evidence cannot release prior reservation" (fun () ->
    let initial=change {operation with Cleanup="unconfirmed"}
    let first=evaluate initial
    for events in [
        [|{operation with Revision=3L;Supersedes=2L;State="accepted";Event="current-observation"}|]
        [|{operation with Revision=2L;Supersedes=1L;State="accepted";Event="current-observation";ObservedAt=now.AddMinutes(-6.)}|]
        [|{operation with Revision=2L;Supersedes=1L;State="accepted";Event="current-observation";ObservedAt=now.AddSeconds(1.)}|]
        [|{operation with Revision=2L;Supersedes=1L;State="accepted";Event="current-observation"};{operation with Revision=2L;Supersedes=1L;State="accepted";Event="current-observation";Evidence="conflict"}|]
    ] do
        let result=evaluate {previous first with Events=events;Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active";InFlight=false}|]}}}
        expect (result.ActiveReservations.Length=1 && result.Current[0].Action<>"return-existing-owner"))
test "single repair predicate fences accepted failed event on active lane after deadline" (fun () ->
    let input={change {operation with State="accepted";ExecutionDeadline=now} with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active"}|]}}}
    action "retain-admission" input)
test "local revision gap marks resynchronization" (fun () -> expect ((evaluate(change {operation with Revision=3L;Supersedes=2L})).Resynchronize))
test "revision-gap multi-cycle current cannot become its own accepted base" (fun () ->
    let first={operation with Revision=3L;Supersedes=2L}
    let mutable current=evaluate(change first)
    for revision in 4L..7L do
        let clean={operation with Revision=revision;Supersedes=revision-1L;Event="current-observation";State="accepted"}
        let other={lane with Id="B";Item="B";OriginalItem="B";Chain="B";State="active";InFlight=false}
        let result=evaluate {previous current with Events=[|clean|];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active";InFlight=false};other|]}}}
        let a=result.Current |> Array.find (fun d -> d.Lane="A")
        expect (a.Action="refresh-facts" && Array.contains "operation-revision-gap" a.Unresolved && result.Resynchronize)
        expect (result.ActiveReservations |> Array.exists (fun l -> l.Id="A"))
        expect (result.Current |> Array.exists (fun d -> d.Lane="B" && Array.contains "shared-surface-conflict" d.Reasons))
        current<-result
    let retainedOnly=evaluate {previous current with Events=[||];Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active";InFlight=false};{lane with Id="B";Item="B";OriginalItem="B";Chain="B";State="active";InFlight=false}|]}}}
    expect (retainedOnly.Resynchronize && Array.contains "operation-revision-gap" retainedOnly.Current[0].Unresolved)
    let chain=[|for revision in 1L..6L -> {operation with Revision=revision;Supersedes=revision-1L}|]
    let resolved=evaluate {previous current with Events=chain;Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active";InFlight=false};{lane with Id="B";Item="B";OriginalItem="B";Chain="B";State="active";InFlight=false}|]}}}
    expect (not resolved.Resynchronize && not (Array.contains "operation-revision-gap" resolved.Current[0].Unresolved)))
test "missing delta base persists until compatible supplied base resynchronizes" (fun () ->
    let initial={spec with Delta={delta with BaseRevision=""}}
    let first=evaluate initial
    expect (Array.contains "missing-delta-base" first.Current[0].Unresolved)
    let second=evaluate {previous first with Events=[||];Delta=delta}
    expect (not second.Resynchronize && not (Array.contains "missing-delta-base" second.Current[0].Unresolved)))
test "same repair predicate fences accepted failed observation after observation deadline" (fun () ->
    let input={change {operation with Class="observation";State="accepted";ObservationDeadline=now} with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active"}|]}}}
    action "refresh-facts" input)
test "table one temporal predicate covers state and event induced repair across operation classes" (fun () ->
    for cls in ["ordinary";"observation";"integration";"protected"] do
        for state,event in ["running","current-observation";"repair-needed","current-observation";
                            "cleanup-observed","current-observation";"accepted","current-observation";"accepted","failed"] do
            for expired in [false;true] do
                let deadline=if expired then now else now.AddMinutes(1.)
                let o={operation with Class=cls;State=state;Event=event;ExecutionDeadline=deadline;ObservationDeadline=deadline}
                let repair=state="repair-needed" || state="cleanup-observed" || event="failed"
                let expected=if cls="protected" then "retain-admission"
                             elif expired && (state<>"accepted" || repair) then
                                 if cls="observation" then "refresh-facts" else "retain-admission"
                             else "return-existing-owner"
                let input={change o with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active"}|]}}}
                action expected input)
test "fresh read-only observation budget can inspect unknown execution budget" (fun () ->
    let input={change {operation with Class="observation";ExecutionMode="unknown";ExecutionDeadline=DateTimeOffset.MinValue;State="running";Event="current-observation"} with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with State="active"}|]}}}
    action "return-existing-owner" input)
test "maximum128 operations plus lane evidence yields replayable current view" (fun () ->
    let input={spec with Events=[|for index in 1..128 -> {operation with Operation="op-"+string index;Evidence="evidence-"+string index}|]}
    let current=evaluate input
    expect (current.Current[0].Operations.Length=128 && current.Current[0].Evidence.Length=129)
    expect ((evaluate {previous current with Events=[||]}).Changed.Length=0))
test "128 retained plus new disjoint stream refuses without dropping originals" (fun () ->
    let first=evaluate {spec with Events=[|for index in 1..128 -> {operation with Operation="op-"+string index;Evidence="same"}|]}
    let next={previous first with Events=[|{operation with Operation="op-129";Evidence="same"}|]}
    refuses (fun () -> evaluate next |> ignore)
    expect (first.Current[0].Operations.Length=128))
test "maximum256 unique current references from lanes and operation facts replay" (fun () ->
    let evidence=[|for index in 1..128 -> "lane-reference-"+string index|]
    let input={spec with Delta={delta with Snapshot={snapshot with Lanes=[|{lane with Evidence=evidence}|]}};Events=[|for index in 1..128 -> {operation with Operation="op-"+string index;Evidence="operation-reference-"+string index}|]}
    let first=evaluate input
    expect (first.Current[0].Evidence.Length=256)
    let next={previous first with Delta=input.Delta;Events=[||]}
    expect ((evaluate next).Changed.Length=0))
test "replay population property over multiple lanes and bounded operations" (fun () ->
    for count in [1;64;128] do
        let lanes=[|for index in 1..min count 64 -> {lane with Id="L"+string index;Item="L"+string index;OriginalItem="L"+string index;Chain="L"+string index;TouchSet=[|"src/L"+string index+"/**"|]}|]
        let events=[|for index in 0..count-1 -> {operation with Lane=lanes[index % lanes.Length].Id;Operation="op-"+string index;Evidence="unique-"+string index}|]
        let input={spec with Delta={delta with Snapshot={snapshot with Lanes=lanes}};Events=events}
        let first=evaluate input
        let next={previous first with Delta=input.Delta;Events=[||]}
        expect ((evaluate next).Current.Length=lanes.Length && (evaluate next).Changed.Length=0))
test "canonical next input byte overflow refuses before successful current emission" (fun () ->
    let terminal={lane with State="closed";InFlight=false;Validation="passed";Projection="landed"}
    let small={change {operation with State="accepted";Event="current-observation"} with Delta={delta with Snapshot={snapshot with Lanes=[|terminal|]}}}
    let padding=262144-Encoding.UTF8.GetByteCount(encode small)
    let input={small with Delta={small.Delta with Snapshot={snapshot with Lanes=[|{terminal with Worktree=terminal.Worktree+String('p',padding)}|]}}}
    expect (Encoding.UTF8.GetByteCount(encode input)=262144)
    expect (try evaluate input |> ignore;false with :? InvalidOperationException as error -> error.Message="routine-current-replay-bound"))
test "unknown operation tags refuse" (fun () -> for o in [{operation with Class="spawn"};{operation with Event="approve"};{operation with AllocationMode="invented"}] do refuses (fun () -> evaluate(change o) |> ignore))
test "event population bounded" (fun () -> refuses (fun () -> evaluate {spec with Events=Array.create 129 operation} |> ignore))
test "compact UTF8 escaping includes newline" (fun () -> let result=evaluate(change {operation with Evidence=String.replicate 200 "é\\\""}) in let output=routineStdout "/private/artifact" hash result in expect (Encoding.UTF8.GetByteCount(output)+1<=2048 && output.Contains "overflow-retrieve-artifact"))
test "exact 2048 output boundary" (fun () -> let result={evaluate spec with Changed=[||]} in let baseline=routineStdout "" hash result in let path=String('p',2047-Encoding.UTF8.GetByteCount baseline) in expect (Encoding.UTF8.GetByteCount(routineStdout path hash result)+1=2048))
test "oversize artifact pointer refuses without slicing" (fun () -> refuses (fun () -> routineStdout (String('p',3000)) hash (evaluate spec) |> ignore))
let temp=Path.Combine(Path.GetTempPath(),"routine-decisions-test-"+Guid.NewGuid().ToString("N"))
Directory.CreateDirectory temp |> ignore
try
    test "strict duplicate unknown JSON fields refuse" (fun () ->
        for tail in [",\"schema\":\"duplicate\"}";",\"mintAuthority\":true}"] do
            let path=Path.Combine(temp,"bad.json")
            File.WriteAllText(path,(encode spec).TrimEnd('}')+tail)
            refuses (fun () -> read<RoutineInput> path |> ignore))
    test "CLI bounds refusal and retains complete artifact" (fun () ->
        let input=Path.Combine(temp,"input.json")
        File.WriteAllText(input,encode(change {operation with Event=String('x',4000)}))
        let original=Console.Out
        use output=new StringWriter()
        let code=try Console.SetOut output;run "routine-decisions" input (Path.Combine(temp,"private")) finally Console.SetOut original
        let stdout=output.ToString()
        expect (code=3 && Encoding.UTF8.GetByteCount stdout<=2048)
        let artifacts=Directory.GetFiles(Path.Combine(temp,"private"),"*.measure.json")
        expect (artifacts.Length=1))
    test "CLI success retains full current and pin before bounded emission" (fun () ->
        let input=Path.Combine(temp,"input.json")
        File.WriteAllText(input,encode spec)
        let original=Console.Out
        use output=new StringWriter()
        let code=try Console.SetOut output;run "routine-decisions" input (Path.Combine(temp,"success")) finally Console.SetOut original
        let stdout=output.ToString()
        expect (code=0 && Encoding.UTF8.GetByteCount stdout<=2048)
        use doc=JsonDocument.Parse stdout
        let path=doc.RootElement.GetProperty("artifact").GetString()
        let expected=doc.RootElement.GetProperty("sha256").GetString()
        expect (digest(File.ReadAllBytes path)=expected)
        let result,_=read<RoutineResult> path
        expect (result.ActiveReservations=[|lane|] && result.Current[0].Operations=[|operation|]))
finally Directory.Delete(temp,true)
printfn "ROUTINE_DECISIONS_ACCEPTED %d cases" passed
