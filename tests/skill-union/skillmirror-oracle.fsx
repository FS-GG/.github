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

// ---------------------------------------------------------------------------------------------
// THE THREE-WAY DIGEST TABLE (`digestVectors`) — FS-GG/.github#1547.
// ---------------------------------------------------------------------------------------------
// The `fixtures` loop above measures `verify`'s three FACTS. This measures the DIGEST, which is the
// surface #1547 exists because nothing covered: the canonical digest has three implementations and
// the pairwise pins that existed skipped it, so CRLF drifted unnoticed until a person read the code.
//
// The library is fed the way its real callers feed it — a decoded BODY STRING, not bytes, via a
// BOM-detecting reader. That is the whole reason a BOM never reaches `sha256`: the caller's decoder
// consumes it. Decoding here rather than pre-stripping the BOM by hand keeps this an observation of
// the library's actual contract instead of a re-implementation of the shells' half of it.
let decodeBody (base64Bytes: string) =
    use stream = new MemoryStream(Convert.FromBase64String base64Bytes)
    use reader = new StreamReader(stream, Text.Encoding.UTF8, detectEncodingFromByteOrderMarks = true)
    reader.ReadToEnd()

let digestVectors =
    match doc.RootElement.TryGetProperty "digestVectors" with
    | true, block -> block.GetProperty("vectors").EnumerateArray() |> List.ofSeq
    | _ -> []

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

// ---------------------------------------------------------------------------------------------
// THE THREE-WAY DIGEST TABLE (`digestVectors`) — FS-GG/.github#1547.
// ---------------------------------------------------------------------------------------------
// The loop above measures `verify`'s three FACTS across two implementations. This measures the
// DIGEST, which is the surface #1547 exists because nothing covered: the canonical digest has THREE
// implementations (this library, `skill_digest` in scripts/skill-union-assert.sh, and
// `canonical_digest` in scripts/fsgg-skill-registry-check) and the pairwise pins that existed both
// skipped it, so a CRLF divergence sat unnoticed until a person read the code.
//
// This half derives the single `digest` value each vector records; skillmirror-conformance.sh then
// holds BOTH shells to it hermetically on every PR. Same division of labour as the `library` column:
// measured here, asserted there.

// An ABSENT or EMPTY digest table is a failure, not a silent skip. This oracle's whole job is to be
// the place the expectations came from; a table it quietly measured nothing from would produce the
// most confident possible green over zero work, which is the #266 shape these gates exist to end.
if List.isEmpty digestVectors then
    eprintfn
        "DISAGREES  the table carries no `digestVectors[]` — .github#1547's three-way digest table is missing or empty."

    disagreements <- disagreements + 1

for fx in digestVectors do
    let name = fx.GetProperty("name").GetString()
    let declared = fx.GetProperty("digest").GetString()
    let measured = SkillMirror.sha256 (decodeBody (fx.GetProperty("bytesBase64").GetString()))

    if declared <> measured then
        disagreements <- disagreements + 1
        eprintfn "DISAGREES  digestVector %s" name
        eprintfn "  table:   %s" declared
        eprintfn "  library: %s" measured
    else
        printfn "agrees     digestVector %s  %s" name measured

// NON-VACUITY, IN BOTH DIRECTIONS. A table whose vectors all share one digest is satisfied by an
// implementation that ignores its input entirely; a table with no COLLIDING pair does not pin the
// CRLF/LF equality that IS #1547's decision. Requiring both stops either property being lost to a
// well-meaning tidy-up of the vector list.
let distinctDigests =
    digestVectors |> List.map (fun fx -> fx.GetProperty("digest").GetString()) |> List.distinct

if not (List.isEmpty digestVectors) then
    if List.length distinctDigests < 2 then
        eprintfn
            "DISAGREES  every digestVector shares one digest — an implementation ignoring its input would pass."

        disagreements <- disagreements + 1

    if List.length distinctDigests = List.length digestVectors then
        eprintfn
            "DISAGREES  no two digestVectors share a digest — nothing pins the CRLF/LF equality #1547 decided."

        disagreements <- disagreements + 1

// The provenance the table carries must be the library this run actually measured, or the table is
// recording a derivation that did not happen.
let srcPath = Path.Combine(libDir, "SkillMirror.fs")
let got = sourceDigest srcPath
let declared = doc.RootElement.GetProperty("derivedFrom").GetProperty("skillMirrorFsSha256").GetString()
let declaredFiles = doc.RootElement.GetProperty("derivedFrom").GetProperty("libraryFiles")

printfn ""
printfn "library source:   %s" srcPath
printfn "sha256 measured:  %s" got
printfn "sha256 in table:  %s" declared

if got <> declared then
    disagreements <- disagreements + 1
    eprintfn "DISAGREES  derivedFrom.skillMirrorFsSha256 is stale — this table was derived from a DIFFERENT library revision."

for file in [ "Schemas.fs"; "SkillMirror.fs" ] do
    let path = Path.Combine(libDir, file)
    let expected = declaredFiles.GetProperty($"src/FS.GG.Contracts/{file}").GetString()
    if sourceDigest path <> expected then
        disagreements <- disagreements + 1
        eprintfn "DISAGREES  derivedFrom.libraryFiles[%s] is stale." file

// `digestVectors` carries its OWN provenance, because it was added (.github#1547) while the
// top-level `derivedFrom` was already stale about a revision #1576 owns re-deriving. A measured
// column whose provenance record is decorative is worth nothing, so it is GRADED here rather than
// merely written down — and separately from `derivedFrom`, so the two can legitimately differ while
// each stays honest about itself.
match doc.RootElement.GetProperty("digestVectors").TryGetProperty "measuredAgainst" with
| true, block ->
    let declaredDigest = block.GetProperty("skillMirrorFsSha256").GetString()

    printfn "digestVectors measuredAgainst: %s" declaredDigest

    if got <> declaredDigest then
        disagreements <- disagreements + 1

        eprintfn
            "DISAGREES  digestVectors.measuredAgainst.skillMirrorFsSha256 (%s) is not the library measured here (%s) — re-run the oracle and reconcile the digest column."
            declaredDigest
            got
| _ ->
    disagreements <- disagreements + 1

    eprintfn
        "DISAGREES  digestVectors has no `measuredAgainst` block — its `digest` column would be a measurement with no record of what it was measured against."

// BOTH POPULATIONS, EACH WITH ITS OWN COUNT (.github#1506's rule, applied to this script's own
// summary): one merged "vectors: N" would let a run that measured every `verify` vector and ZERO
// digest vectors print a number that looks like full coverage.
printfn
    "verify vectors: %d, digest vectors: %d, disagreements: %d"
    (List.length fixtures)
    (List.length digestVectors)
    disagreements
exit (if disagreements = 0 then 0 else 1)
