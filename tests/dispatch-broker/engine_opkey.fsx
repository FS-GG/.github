// THE ENGINE, EXECUTED — the oracle half of `tests/dispatch-broker`'s transcription differential.
//
// WHY THIS FILE EXISTS. `.github/workflows/fsgg-dispatch-broker.yml` carries a SECOND implementation of
// `Operation.preimage` (src/FS.GG.Coord.Core/Operation.fs), in Python, because a GitHub runner cannot
// reach the engine's types without putting a compile on the critical path of every dispatch. Two
// implementations of one digest that must agree byte-for-byte or NO DEDUPE EVER MATCHES is a defect
// waiting for the day they drift. Until now the fixture pinned that agreement with a `grep` for the
// separator literal and the `String.concat` line — which reds on a reformat and stays green on a
// semantic change, and which the board analyst named as insufficient when it reopened `.github#2720`:
// "either have the broker call the engine, or add a test that fails when the transcription and
// `Operation.preimage` disagree. A grep-based constant leg is not that test."
//
// This is that test's oracle. It EXECUTES `Operation.compose` — the shipped, public, production
// function — over a corpus supplied by `differential.py`, and prints what the engine actually decides.
// `differential.py` runs the shipped broker script over the same corpus and compares. Neither side is
// re-typed: one is the engine's own assembly, the other is the `run:` block lifted out of the real
// workflow.
//
// THE WIRE ENCODING IS UTF-16 CODE UNITS, IN HEX, AND THAT IS NOT DECORATION. Two of the domain's six
// refusals are about ill-formed text — an unpaired surrogate is by definition NOT expressible in UTF-8,
// so a UTF-8 file, a UTF-8 argv and a UTF-8 environment variable can none of them carry the corpus
// faithfully. A `.NET` string and a Python `str` are both sequences of UTF-16 code points that MAY be
// lone surrogates, so the corpus travels as `,`-separated hex code units and is reassembled exactly on
// each side. An encoding that quietly repaired the input would make the surrogate legs vacuous, which
// is the `#266` class this whole slice exists to close.
//
// PROTOCOL. stdin: one vector per line, TAB-separated, six fields:
//     id <TAB> itemUnits <TAB> genUnits <TAB> receiverUnits <TAB> opKind <TAB> payloadUnits
// where `opKind` is `merge`, `dispatch` or `publish`, and each `*Units` field is a comma-separated list
// of hex UTF-16 code units (an EMPTY field is the empty string). stdout: one answer per line,
//     id <TAB> ok      <TAB> <64 lowercase hex> <TAB> <wire spelling units>
//     id <TAB> refused <TAB> <machine refusal classes, comma-separated, in the engine's own order>
// Refusal classes are derived from the `Refusal` DU by a match with NO wildcard arm, so a seventh
// refusal case added to the engine breaks THIS file's build rather than being silently reported as
// something else.

// THE ASSEMBLY ARRIVES ON THE COMMAND LINE, AND THERE IS NO `#r` HERE ON PURPOSE (.github#2653).
//
// A `#r` would have to name a path inside the checkout, which means something must BUILD the engine
// into the checkout — and `scripts/fsgg-coord`'s tier 2a then prefers that artifact over the shared
// checkout's engine and `stale_guard` fail-closes EVERY board write from that worktree, for a build the
// worker never asked for. `tests/engine-build-siting` is the gate that refuses exactly this, and it
// caught the first head of this change doing it.
//
// So `differential.py` obtains the assembly through `scripts/build-gate-engine` — the one sanctioned
// route, which sites both `bin/` and `obj/` outside the checkout entirely — and passes it here as
// `dotnet fsi -r:<path>`. Running this script by hand without that reference is a compile error naming
// `FS.GG.Coord`, which is the correct failure: it says the oracle is missing rather than silently
// grading nothing.

open System
open FS.GG.Coord

/// Reassemble a component from its hex UTF-16 code units. `String(char[])` is the only constructor that
/// admits a lone surrogate; anything routed through an encoder would substitute U+FFFD and destroy the
/// very distinction the `UnpairedSurrogate` refusal exists to make.
let decode (field: string) : string =
    if field = "" then
        ""
    else
        field.Split(',')
        |> Array.map (fun unit -> char (Convert.ToInt32(unit, 16)))
        |> String

/// The inverse of `Operation.wire`, and it is CHECKED rather than assumed: every `ok` answer below
/// carries the engine's own `wire op` back to the comparer, which asserts it equals the string the
/// broker was driven with. Without that check this function would be a third implementation of the
/// operation vocabulary, quietly deciding what the other two were compared on.
let buildOp (kind: string) (payload: string) : Operation.Op =
    match kind with
    | "merge" -> Operation.Merge
    | "dispatch" -> Operation.Dispatch payload
    | "publish" -> Operation.Publish payload
    | other -> failwithf "unknown op kind %s — the corpus and this oracle disagree, which is a no-verdict" other

/// A stable machine name per refusal case. NO WILDCARD ARM: `Refusal` is a closed DU, and adding a case
/// to the engine must break this build (FS0025 is an error under this repository's settings) rather
/// than fall through to a catch-all that would report the new case as an old one.
let refusalClass (refusal: Operation.Refusal) : string =
    let part (p: Operation.Part) =
        match p with
        | Operation.Item -> "item"
        | Operation.Generation -> "generation"
        | Operation.Receiver -> "receiver"
        | Operation.OperationPayload -> "payload"

    match refusal with
    | Operation.Blank p -> "blank:" + part p
    | Operation.ControlCharacter p -> "control:" + part p
    | Operation.UnpairedSurrogate p -> "surrogate:" + part p
    | Operation.ItemNotFullyQualified _ -> "itemNotQualified"
    | Operation.ReceiverNotFullyQualified _ -> "receiverNotQualified"
    | Operation.GenerationNotServerAssigned _ -> "generationNotServerAssigned"

/// Hex UTF-16 code units, the same encoding the corpus arrives in, so the wire spelling can travel back
/// to the comparer without a UTF-8 round trip it might not survive.
let encode (value: string) : string =
    value |> Seq.map (fun ch -> (int ch).ToString("x4")) |> String.concat ","

let answer (line: string) : string =
    let fields = line.Split('\t')

    if fields.Length <> 6 then
        failwithf "a corpus line carried %d fields, not 6 — no verdict" fields.Length

    let id = fields[0]
    let op = buildOp fields[4] (decode fields[5])

    match Operation.compose (decode fields[1]) (decode fields[2]) (decode fields[3]) op with
    | Ok key -> sprintf "%s\tok\t%s\t%s" id key.Value (encode (Operation.wire op))
    | Error refusals -> sprintf "%s\trefused\t%s" id (refusals |> List.map refusalClass |> String.concat ",")

Console.In.ReadToEnd().Split('\n')
|> Array.filter (fun line -> line.Trim() <> "")
|> Array.iter (answer >> Console.Out.WriteLine)
