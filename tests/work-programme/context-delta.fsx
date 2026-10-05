#load "../../.agents/skills/work-programme/scripts/programme.fsx"
open System
open System.IO
open System.Text
open Programme

let now = DateTimeOffset.Parse "2026-10-05T04:56:05Z"
let hash = String('a',64)
let lane name : Lane = {
    Id=name;Feature="FEATURE";Item=name;OriginalItem=name;Attempt="attempt";Repository="FS-GG/Example"
    Chain=name;Owner="owner";Worktree="/synthetic/worktree";Head="candidate";Priority=1;Readable=true
    ObservedAt=now;Evidence=[|"synthetic-native-reference"|];State="active";InFlight=true
    TouchSet=[|"src/"+name+"/**"|];Dependencies=[||];Source="local";Validation="pending"
    Publication="not-required";Installed="pending";Native="unknown";Projection="not-required" }
let a,b=lane "A",lane "B"
let snapshot : Snapshot = {Schema="fsgg.programme.snapshot/1";Campaign="campaign";Capacity=2;CapacityKnown=true;MaxAgeSeconds=300;Lanes=[|a;b|];OpenPulls=[||]}
let row name revision : LaneReturn = {
    Schema="fsgg.programme.lane-return/1";Campaign="campaign";Lane=name;Feature="FEATURE";Item=name;OriginalItem=name
    OriginalAttempt="original-attempt";Attempt="attempt";Candidate="candidate";Owner="owner"
    Revision=revision;Supersedes=revision-1L;SourceRevision="candidate";InputPacketSha256=hash;ObservedAt=now
    Outcome="window-reported";Boundaries=returnBoundaries (lane name)
    Evidence=[|{Kind="source";Reference="artifact-reference";Sha256=hash;Scope="source-only"}|]
    Unknowns=[|"native usage unknown"|];Exception="none";Continuation="retain owner; native boundary remains unknown";Narrative="source checkpoint" }
let old,newer = row "A" 1L,row "A" 2L
let spec : DeltaInput = {
    Schema="fsgg.programme.delta-input/1";EvaluationTime=now;EvaluatorIdentity="programme-delta-1";PolicyIdentity="policy-1";UserScopeRevision="scope-1"
    BaseRevision="view-1";CurrentRevision="view-2";BaseEvaluatorIdentity="programme-delta-1";BasePolicyIdentity="policy-1";BaseUserScopeRevision="scope-1"
    BaseReturns=[|old|];CurrentReturns=[|newer|];Snapshot=snapshot }
let delta value = contextDelta hash value
let has kind result = result.Notices |> Array.exists (fun notice -> notice.Kind=kind)
let expect condition = if not condition then failwith "unexpected result"
let refuses action = let refused = try action();false with _ -> true in expect refused
let mutable passed=0
let test name action = action();passed<-passed+1;printfn "PASS %s" name

test "explicit time deterministic replay" (fun () -> expect (encode(delta spec)=encode(delta spec)))
test "six boundaries and original identities retained" (fun () ->
    let result=delta spec
    expect (result.Changed=[|newer|] && result.Changed[0].Boundaries=returnBoundaries a && result.Changed[0].OriginalAttempt="original-attempt"))
test "duplicate return idempotent" (fun () -> expect (encode(delta {spec with CurrentReturns=[|newer;newer|]})=encode(delta spec)))
test "out-of-order within same stream deterministic" (fun () -> expect (encode(delta {spec with CurrentReturns=[|newer;old|]})=encode(delta {spec with CurrentReturns=[|old;newer|]})))
test "older return cannot overwrite base" (fun () ->
    let result=delta {spec with BaseReturns=[|newer|];CurrentReturns=[|old|]}
    expect (result.Changed.Length=0 && has "older-return-retained-as-history" result))
test "same revision conflict despite success narrative" (fun () ->
    let result=delta {spec with CurrentReturns=[|newer;{newer with Narrative="all success"}|]}
    expect (result.Changed.Length=0 && has "equal-revision-conflict-reconcile" result))
test "equal revision cross-base conflict" (fun () ->
    let result=delta {spec with CurrentReturns=[|{old with Narrative="all success"}|]}
    expect (result.Changed.Length=0 && has "equal-revision-conflict-reconcile" result))
test "conflicted base cannot silently accept current" (fun () ->
    let result=delta {spec with BaseReturns=[|old;{old with Narrative="conflict"}|]}
    expect (result.Changed.Length=0 && has "conflicted-base-or-return-reconcile" result))
test "different owner incomparable and current snapshot join wins" (fun () ->
    let foreign={newer with Owner="superseded-owner";Revision=99L;Supersedes=98L}
    let result=delta {spec with CurrentReturns=[|foreign;newer|]}
    expect (result.Changed=[|newer|] && has "superseded-or-incomparable-owner-candidate" result &&
            encode result=encode(delta {spec with CurrentReturns=[|newer;foreign|]})))
test "different candidate cannot overwrite current" (fun () ->
    let result=delta {spec with CurrentReturns=[|{newer with Candidate="superseded"}|]}
    expect (result.Changed.Length=0 && has "superseded-or-incomparable-owner-candidate" result))
test "original attempt conflict reconciles" (fun () ->
    let result=delta {spec with CurrentReturns=[|{newer with OriginalAttempt="invented"}|]}
    expect (result.Changed.Length=0 && has "original-lineage-conflict-reconcile" result))
test "missing base keeps full active inventory without delta" (fun () ->
    let result=delta {spec with BaseRevision="";BaseReturns=[||]}
    expect (result.Resynchronize && result.Changed.Length=0 && result.ActiveReservations=[|a;b|]))
test "missing intermediate return requests resync" (fun () ->
    let result=delta {spec with CurrentReturns=[|{newer with Revision=3L;Supersedes=2L}|]}
    expect (result.Resynchronize && result.Changed.Length=0 && has "missing-return-base-resynchronize" result))
test "scope change invalidates base" (fun () ->
    let result=delta {spec with UserScopeRevision="scope-2"}
    expect (result.Resynchronize && result.Changed.Length=0 && has "scope-policy-evaluator-invalidated" result))
test "policy or evaluator change invalidates base" (fun () ->
    for changed in [{spec with PolicyIdentity="policy-2"};{spec with EvaluatorIdentity="programme-delta-2"}] do expect ((delta changed).Resynchronize))
test "advancing evaluation time expires unchanged source" (fun () ->
    let result=delta {spec with EvaluationTime=now.AddMinutes(6.)}
    expect (result.Changed.Length=0 && result.ActiveReservations=[|a;b|] && has "stale-or-unreadable-fact-refresh" result))
test "copying unreadable snapshot does not refresh fact" (fun () ->
    let result=delta {spec with Snapshot={snapshot with Lanes=[|{a with Readable=false};b|]}}
    expect (result.Changed.Length=0 && result.ActiveReservations.Length=2))
test "future return refuses current projection" (fun () -> expect ((delta {spec with CurrentReturns=[|{newer with ObservedAt=now.AddSeconds(1.)}|]}).Changed.Length=0))
test "acknowledgment cannot mint completion" (fun () ->
    let result=delta {spec with CurrentReturns=[|{newer with Outcome="acknowledgment"}|]}
    expect (result.Changed.Length=0 && has "acknowledgment-not-completion" result && result.ActiveReservations.Length=2))
test "owner success cannot upgrade snapshot boundaries" (fun () ->
    let result=delta {spec with CurrentReturns=[|{newer with Boundaries={newer.Boundaries with Native="succeeded";Validation="passed"}}|]}
    expect (result.Changed.Length=0 && has "boundary-correspondence-reconcile" result))
test "partial return set cannot hide active owner" (fun () -> expect ((delta {spec with CurrentReturns=[||]}).ActiveReservations=[|a;b|]))
test "late coherent failure fences dependency" (fun () ->
    let disputed={a with Validation="disputed";Source="merged";State="closed"}
    let result=delta {spec with Snapshot={snapshot with Lanes=[|disputed;b|]};CurrentReturns=[|{newer with Boundaries=returnBoundaries disputed}|]}
    expect (has "late-failure-dependent-acceptance-fenced" result && result.ActiveReservations.Length=2))
test "same view revision cannot hide changed content" (fun () ->
    let result=delta {spec with CurrentRevision=spec.BaseRevision}
    expect (result.Changed.Length=0 && has "view-revision-content-conflict-reconcile" result))
test "missing mandatory return evidence refuses" (fun () -> refuses (fun () -> delta {spec with CurrentReturns=[|{newer with Evidence=[||]}|]} |> ignore))
test "bounded return narrative refuses overflow" (fun () -> refuses (fun () -> delta {spec with CurrentReturns=[|{newer with Narrative=String('x',2049)}|]} |> ignore))
test "unknown outcome cannot imply complete" (fun () -> refuses (fun () -> delta {spec with CurrentReturns=[|{newer with Outcome="completed"}|]} |> ignore))
test "foreign campaign and item lineage refuse" (fun () ->
    for bad in [{newer with Campaign="other"};{newer with OriginalItem="other"}] do refuses (fun () -> delta {spec with CurrentReturns=[|bad|]} |> ignore))
test "return population bound" (fun () -> refuses (fun () -> delta {spec with CurrentReturns=Array.create 129 newer} |> ignore))
let temp=Path.Combine(Path.GetTempPath(),"context-delta-"+Guid.NewGuid().ToString("N")+".json")
try
    test "unknown JSON fields refuse additive command" (fun () ->
        File.WriteAllText(temp,(encode spec).TrimEnd('}')+",\"authorizeEffect\":true}")
        refuses (fun () -> read<DeltaInput> temp |> ignore))
    test "duplicate JSON fields refuse additive command" (fun () ->
        File.WriteAllText(temp,(encode spec).TrimEnd('}')+",\"schema\":\"duplicate\"}")
        refuses (fun () -> read<DeltaInput> temp |> ignore))
finally
    if File.Exists temp then File.Delete temp
test "absent stream cannot establish superseded revision" (fun () ->
    let result=delta {spec with BaseReturns=[||]}
    expect (result.Resynchronize && result.Changed.Length=0 && has "missing-return-base-resynchronize" result))
test "fresh stream revision1 supersedes0 remains valid" (fun () ->
    let result=delta {spec with BaseReturns=[||];CurrentReturns=[|old|]}
    expect (not result.Resynchronize && result.Changed=[|old|]))
test "two current original attempts reconcile instead of parallel updates" (fun () ->
    let other={old with OriginalAttempt="other-original"}
    let result=delta {spec with BaseReturns=[||];CurrentReturns=[|old;other|]}
    expect (result.Changed.Length=0 && has "original-lineage-conflict-reconcile" result &&
            encode result=encode(delta {spec with BaseReturns=[||];CurrentReturns=[|other;old|]})))
test "newer return cannot hide lower revision cross-base conflict" (fun () ->
    let result=delta {spec with CurrentReturns=[|newer;{old with Narrative="conflicting lower revision"}|]}
    expect (result.Changed.Length=0 && has "equal-revision-conflict-reconcile" result))
printfn "PASS %d context delta controls; no effects dispatched" passed
