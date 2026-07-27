// DERIVATION ORACLE for tests/skill-union/skillmirror.fixtures.json — FS-GG/.github#1513.
//
// WHY THIS EXISTS. .github#120 settled that `Fsgg.SkillMirror` (FS.GG.Contracts) is ADR-0014's "one
// implementation" and that `scripts/skill-union-assert.sh` FOLLOWS it. Nothing enforced that, and the
// two diverged three times — #1506 closed two of them, #1513 the third — each found by a person
// reading the code, after it had already misdirected real work.
//
// `skillmirror-conformance.sh` closes the loop by pinning the shell against a SHARED VECTOR TABLE (the
// #398 pattern). A table is worth exactly as much as where its expected column came from, and this is
// where it came from: this script runs `SkillMirror.verify` — THE LIBRARY'S OWN SOURCE, `#load`ed by
// `skillmirror-oracle.sh` — over every vector in the table and reports, per vector, whether the
// checked-in `library` block matches what the library actually returns. The expectations are MEASURED,
// not transcribed from the `.fsi` comments, which is the precise failure mode #1513 is about.
//
// Run it through the wrapper, which supplies the `#load`s:
//
//   bash tests/skill-union/skillmirror-oracle.sh --lib <dir containing SkillMirror.fs>
//
// Exit 0 = every vector's `library` block is what the library returns. Exit 1 = at least one
// disagreement, printed as table-vs-library so the table can be corrected by hand. It is deliberately a
// CHECKER and not a writer: a generator that rewrote its own expectations from the implementation would
// green any divergence the moment it was regenerated, which is the shape of the defect, not a fix for it.
//
// WHY THE CI SUITE DOES NOT RUN THIS. `Fsgg.SkillMirror` lives in FS.GG.SDD and ships as a NuGet
// package; running it from this repo's fixture on every PR means a cross-repo checkout or a package
// restore. #1513's criterion 3 names that trade explicitly and asks for a decision rather than an
// assumption. The decision taken: the CONFORMANCE leg is hermetic and shell-side, so it runs everywhere
// and can never be skipped; the DERIVATION is this script, committed and re-runnable by anyone holding
// the library, and the table records the library revision + source digest it was derived from. What
// that does NOT do is notice a FUTURE library change by itself — the residual gap is stated in the
// table's `derivedFrom` block and in docs/coordination/skill-union-assertion.md, and tracked as an
// issue rather than left implied.

open System
open System.IO
open System.Text.Json
open Fsgg

let argv = fsi.CommandLineArgs |> Array.toList |> List.tail

let argValue name =
    let rec go =
        function
        | a :: v :: _ when a = name -> Some v
        | _ :: rest -> go rest
        | [] -> None

    go argv

let libDir =
    match argValue "--lib" with
    | Some d -> d
    | None -> failwith "--lib <dir containing SkillMirror.fs> is required"

let tablePath =
    match argValue "--table" with
    | Some t -> t
    | None -> failwith "--table <skillmirror.fixtures.json> is required"

let doc = JsonDocument.Parse(File.ReadAllText tablePath)

let scopeOf =
    function
    | "process" -> Schemas.SkillScope.Process
    | _ -> Schemas.SkillScope.Product

let strList (e: JsonElement) =
    e.EnumerateArray() |> Seq.map (fun x -> x.GetString()) |> List.ofSeq

/// One vector -> the library's own three facts, by running `verify` on it.
///
/// `verify` takes root LABELS, not paths (it never calls `skillPath`), so the vectors carry the
/// shell's root spelling (".claude/skills") verbatim and no mapping is needed or performed — the two
/// sides are compared over identical strings.
let derive (fx: JsonElement) =
    let id = fx.GetProperty("id").GetString()
    let roots = fx.GetProperty("roots") |> strList
    let bodies = fx.GetProperty("bodies")

    let actual: SkillMirror.ActualCopy list =
        roots
        |> List.map (fun r ->
            let body =
                match bodies.TryGetProperty r with
                | true, v -> Some(v.GetString())
                | _ -> None

            { Root = r; Id = id; Body = body })

    let expected: SkillMirror.ExpectedSkill list =
        [ { Id = id
            Scope = scopeOf (fx.GetProperty("scope").GetString())
            Sha256 = fx.GetProperty("sha256").GetString() } ]

    match SkillMirror.verify roots expected actual with
    | [] -> (([]: string list), false, ([]: string list))
    | drift :: _ -> (drift.MissingRoots, drift.Divergent, drift.HashMismatchRoots)

let sourceDigest (path: string) =
    File.ReadAllBytes path
    |> System.Security.Cryptography.SHA256.HashData
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

let quote (s: string) = JsonSerializer.Serialize(s)
let arr (xs: string list) = "[" + (xs |> List.map quote |> String.concat ", ") + "]"

let block (missing, divergent, mismatch) =
    sprintf
        "\"library\": { \"missingRoots\": %s, \"divergent\": %b, \"hashMismatchRoots\": %s }"
        (arr missing)
        divergent
        (arr mismatch)

let fixtures =
    doc.RootElement.GetProperty("fixtures").EnumerateArray() |> List.ofSeq

let mutable disagreements = 0

for fx in fixtures do
    let name = fx.GetProperty("name").GetString()
    let missing, divergent, mismatch = derive fx

    let want = fx.GetProperty("library")

    let wanted =
        (want.GetProperty("missingRoots") |> strList,
         want.GetProperty("divergent").GetBoolean(),
         want.GetProperty("hashMismatchRoots") |> strList)

    if wanted <> (missing, divergent, mismatch) then
        disagreements <- disagreements + 1
        eprintfn "DISAGREES  %s" name
        eprintfn "  table:   %s" (block wanted)
        eprintfn "  library: %s" (block (missing, divergent, mismatch))
    else
        printfn "agrees     %s  %s" name (block (missing, divergent, mismatch))

// The provenance the table carries must be the library this run actually measured, or the table is
// recording a derivation that did not happen.
let srcPath = Path.Combine(libDir, "SkillMirror.fs")
let got = sourceDigest srcPath
let declared = doc.RootElement.GetProperty("derivedFrom").GetProperty("skillMirrorFsSha256").GetString()

printfn ""
printfn "library source:   %s" srcPath
printfn "sha256 measured:  %s" got
printfn "sha256 in table:  %s" declared

if got <> declared then
    disagreements <- disagreements + 1
    eprintfn "DISAGREES  derivedFrom.skillMirrorFsSha256 is stale — this table was derived from a DIFFERENT library revision."

printfn "vectors: %d, disagreements: %d" (List.length fixtures) disagreements
exit (if disagreements = 0 then 0 else 1)
