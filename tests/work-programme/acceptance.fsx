#load "../../.agents/skills/work-programme/scripts/programme.fsx"
open System
open System.IO
open System.Text
open Programme

let now = DateTimeOffset.UtcNow
let root = Path.Combine(Path.GetTempPath(), "work-programme-acceptance-" + Guid.NewGuid().ToString("N"))
Directory.CreateDirectory root |> ignore
let mutable passed = 0
let test name action = action(); passed <- passed + 1; printfn "PASS %s" name
let expect condition = if not condition then failwith "unexpected result"
let refuses action = let refused = try action(); false with _ -> true in expect refused
let lane name : Lane = {
    Id = name; Feature = "FEATURE"; Item = name; OriginalItem = name; Attempt = "attempt"
    Repository = "FS-GG/Example"; Chain = name; Owner = "owner-" + name; Worktree = root; Head = "head"
    Priority = 1; Readable = true; ObservedAt = now; Evidence = [|"native-reference"|]
    State = "ready"; InFlight = false; TouchSet = [|"src/" + name + "/**"|]; Dependencies = [||]
    Source = "local"; Validation = "pending"; Publication = "not-required"; Installed = "not-required"
    Native = "not-required"; Projection = "not-required" }
let snapshot lanes : Snapshot = { Schema = "fsgg.programme.snapshot/1"; Campaign = "campaign"; Capacity = 2;
                                 CapacityKnown = true; MaxAgeSeconds = 300; Lanes = lanes; OpenPulls = [||] }
let actions spec = (frontier now "input" spec).Actions |> Array.map (fun action -> action.Lane, action.Action) |> Map.ofArray
let first spec = (actions spec).["A"]
let a, b = lane "A", lane "B"
try
    test "independent lanes fill capacity" (fun () -> expect (actions (snapshot [|a;b|]) |> Map.forall (fun _ value -> value = "assign")))
    test "selected touch-set reserves against next candidate" (fun () -> expect ((actions (snapshot [|a; {b with TouchSet = a.TouchSet}|])).["B"] = "wait"))
    test "repository namespaces keep identical paths independent" (fun () -> expect ((actions (snapshot [|a; {b with Repository = "FS-GG/Other"; TouchSet = a.TouchSet}|])).["B"] = "assign"))
    test "unknown active owner retains capacity and path" (fun () ->
        let observed = frontier now "input" (snapshot [|{a with InFlight = true; Readable = false}; {b with TouchSet = a.TouchSet}|])
        expect (observed.Active = [|"A"|] && observed.Actions |> Array.exists (fun row -> row.Lane = "B" && row.Action = "wait")))
    test "stale read refuses assignment" (fun () -> expect (first (snapshot [|{a with ObservedAt = now.AddMinutes(-6.)}|]) = "refresh"))
    test "future read refuses assignment" (fun () -> expect (first (snapshot [|{a with ObservedAt = now.AddSeconds(1.)}|]) = "refresh"))
    test "unknown effect reconciles instead of rerunning" (fun () -> expect (first (snapshot [|{a with Native = "unknown"}|]) = "reconcile"))
    test "failed native attempt requires repair before readmission" (fun () -> expect (first (snapshot [|{a with Native = "failed"}|]) = "repair"))
    test "same chain cannot receive two new owners" (fun () -> expect ((actions (snapshot [|a; {b with Chain = "A"}|])).["B"] = "wait"))
    test "delivered source keeps pending validation visible" (fun () -> expect (first (snapshot [|{a with State = "closed"; Source = "merged"}|]) = "watch"))
    test "late coherent dispute blocks installed dependent" (fun () ->
        let spec = snapshot [|{a with State = "closed"; Source = "merged"; Installed = "qualified"; Validation = "disputed"};
                              {b with Dependencies = [|{Lane = "A"; Boundary = "installed"}|]}|]
        expect ((actions spec).["B"] = "wait"))
    test "source readiness does not require native adoption" (fun () ->
        let spec = snapshot [|{a with State = "closed"; Source = "merged"; Native = "pending"};
                              {b with Dependencies = [|{Lane = "A"; Boundary = "source"}|]}|]
        expect ((actions spec).["B"] = "assign"))
    test "same chain waits for section zero projection" (fun () ->
        expect ((actions (snapshot [|{a with State = "closed"; Projection = "pending"}; {b with Chain = "A"}|])).["B"] = "wait"))
    test "existing PR selects repair instead of duplicate" (fun () ->
        expect (first {snapshot [|a|] with OpenPulls = [|{Repository=a.Repository; Campaign="campaign"; Chain="A"; Head="head"}|]} = "resume-pr"))
    test "managed PR capacity spans campaigns" (fun () ->
        let pulls = [|{Repository=a.Repository; Campaign="other"; Chain="X"; Head="head"}; {Repository=a.Repository; Campaign="other"; Chain="Y"; Head="head"}|]
        expect (first {snapshot [|a|] with OpenPulls = pulls} = "prepare-local"))
    test "unobserved capacity cannot admit work" (fun () -> expect (first {snapshot [|a|] with CapacityKnown=false} = "wait"))
    test "dependency cycle refuses instead of deadlocking" (fun () -> refuses (fun () ->
        actions (snapshot [|{a with Dependencies=[|{Lane="B";Boundary="source"}|]}; {b with Dependencies=[|{Lane="A";Boundary="source"}|]}|]) |> ignore))
    test "unsupported glob never appears disjoint" (fun () -> refuses (fun () -> actions (snapshot [|{a with TouchSet=[|"src/*/Types.fs"|]}|]) |> ignore))
    let file = Path.Combine(root, "instruction.md")
    File.WriteAllText(file, "mandatory governing instructions")
    let bytes = File.ReadAllBytes file
    let reference : Reference = {Path=file;Sha256=digest bytes;StartLine=0;EndLine=0;Mandatory=true;Trust="instruction";Reason="governing"}
    let spec : Packet = {Schema="fsgg.programme.packet/1";Lane="A";Objective="bounded work";Stop="accepted window";
                        MandatoryPaths=[|file|];MaximumBytes=1024;References=[|reference|]}
    test "actual packet includes complete governing bytes" (fun () -> let text,selected,_=packet spec in expect (text.Contains "mandatory governing instructions" && selected=[|file|]))
    test "missing governing reference refuses" (fun () -> refuses (fun () -> packet {spec with MandatoryPaths=[|"/missing"|]} |> ignore))
    test "governing excerpt refuses" (fun () -> refuses (fun () -> packet {spec with References=[|{reference with StartLine=1;EndLine=1}|]} |> ignore))
    test "changed file invalidates pinned packet" (fun () ->
        File.AppendAllText(file, " changed")
        refuses (fun () -> packet spec |> ignore)
        File.WriteAllBytes(file, bytes))
    test "oversized mandatory instructions refuse" (fun () ->
        File.WriteAllText(file, String('x', 2048))
        refuses (fun () -> packet {spec with References=[|{reference with Sha256=digest(File.ReadAllBytes file)}|]} |> ignore)
        File.WriteAllBytes(file, bytes))
    let optional = Path.Combine(root, "optional.md")
    File.WriteAllText(optional, String('x',2048))
    test "optional omission retains its reference" (fun () ->
        let _,selected,omitted = packet {spec with References=[|{reference with Path=optional;Sha256=digest(File.ReadAllBytes optional);Mandatory=false;Trust="data"};reference|]}
        expect (selected=[|file|] && omitted=[|optional|]))
    let evidence : Verification = {Schema="fsgg.programme.verification/1";Claim="declared byte equality only";RequiredPaths=[|file|];
                                   Artifacts=[|{Path=file;Sha256=digest bytes;Bytes=int64 bytes.Length}|]}
    test "real evidence verifies with zero mismatch" (fun () -> expect ((verify evidence).Failures.Length = 0))
    test "missing required evidence refuses" (fun () -> refuses (fun () -> verify {evidence with RequiredPaths=[|optional|]} |> ignore))
    test "changed evidence remains a failure" (fun () ->
        File.AppendAllText(file," changed")
        expect ((verify evidence).Failures=[|file|])
        File.WriteAllBytes(file,bytes))
    test "symlink evidence refuses" (fun () ->
        let link=Path.Combine(root,"link")
        File.CreateSymbolicLink(link,file) |> ignore
        expect ((verify {evidence with RequiredPaths=[|link|];Artifacts=[|{evidence.Artifacts[0] with Path=link}|]}).Failures=[|link|]))
    test "duplicate JSON keys refuse" (fun () ->
        let input=Path.Combine(root,"duplicate.json")
        File.WriteAllText(input,"{\"schema\":\"first\",\"schema\":\"second\"}")
        refuses (fun () -> read<Snapshot> input |> ignore))
    test "unknown JSON property refuses" (fun () ->
        let input=Path.Combine(root,"unknown.json")
        File.WriteAllText(input,(encode (snapshot [|a|])).TrimEnd('}') + ",\"authorizeEffect\":true}")
        refuses (fun () -> read<Snapshot> input |> ignore))
    test "deterministic replay returns the same frontier" (fun () -> expect (encode(frontier now "input" (snapshot [|a;b|])) = encode(frontier now "input" (snapshot [|a;b|]))))
    printfn "WORK_PROGRAMME_ACCEPTED %d cases" passed
finally
    Directory.Delete(root,true)
