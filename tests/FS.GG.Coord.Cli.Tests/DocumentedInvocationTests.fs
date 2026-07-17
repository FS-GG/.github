namespace FS.GG.Coord.Cli.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FS.GG.Coord.Cli.Options

/// THE PRESCRIBED-INVOCATION GATE (#919).
///
/// `CommandSurfaceTests` (#869) asserts every verb the engine must KNOW, and it works: deleting `add`
/// would now cost a line in a diff. But it inventories NAMES. `say` sails through it — the verb exists,
/// dispatches, and is in the surface list — while the ARGUMENTS the corpus prescribes are refused:
///
///     $ scripts/fsgg-coord say .github#889 --to brant-0666 'I need src/Audio; can you land first?'
///     fsgg-coord-engine: say: an issue ref takes exactly one argument (got 2).
///
/// Seven prescribing sites documented that line. The parser took `--message`. Nothing paired the two,
/// so the port could re-type a verb's arguments and every test stayed green.
///
/// This is the third instance of ONE defect, each found by hand, mid-protocol, by the worker it bit:
///
///   * #861 — `add` was never ported.                    Verb ABSENT.        (caught by #869's gate)
///   * #867 — `release --status` parsed and ignored.     Flag UNREAD.
///   * #919 — `say`'s documented arg shape never ported. Arguments RE-TYPED. (this)
///
/// #867's own thread proposed this gate and it was never built, so instance 3 arrived on schedule. The
/// recipe's rule applies to the recipe's own tooling: *if a fix keeps regenerating the same finding, the
/// finding is not the bug — the thing that regenerates it is.* This file is that thing.
///
/// WHY THIS IS A PARSER TEST AND NOT A SCRIPT THAT RUNS THE ENGINE, which is what #867 proposed:
/// **you cannot execute a documented invocation to find out whether it parses.** Half the corpus's
/// prescribed lines are WRITES — `say` posts a comment, `claim` takes a lock, `set-field` mutates the
/// board. Running them to check their arguments would post junk to live issues under whatever
/// credentials the runner holds. (Measured, while writing this: `GH_TOKEN=invalid` does not save you —
/// the engine resolves auth through `gh`'s stored credentials and ignores it. Three junk comments
/// landed on a live issue before the mistake was caught.) `Options.parse` is total, pure, and touches
/// no network, so it is the ONE place a documented invocation can be checked safely.
///
/// SCOPE, and its honest limit: this gate checks SHAPE — that the corpus's line reaches the verb with
/// arguments the parser accepts. It cannot check meaning. A `widen --paths "a, b"` (one quoted
/// comma-joined token) parses perfectly and reserves nothing, and this gate is silent on it, correctly:
/// that defect lives past the parser. `PathsCoherence` and the `paths-coherence` gate own that half.
module DocumentedInvocationTests =

    /// Walk up from the test binary to the repo root. The tests run from
    /// `tests/FS.GG.Coord.Cli.Tests/bin/<cfg>/<tfm>/`, and both the corpus roots and this file move
    /// together, so a sentinel that names TWO of the roots cannot match a random ancestor.
    let private repoRoot =
        let rec up (d: DirectoryInfo) =
            if isNull (box d) then
                failwith
                    "DocumentedInvocationTests: no repo root above the test binary (looked for a directory holding both `docs/coordination` and `.claude/skills`)."
            elif Directory.Exists(Path.Combine(d.FullName, "docs", "coordination"))
                 && Directory.Exists(Path.Combine(d.FullName, ".claude", "skills")) then
                d.FullName
            else
                up d.Parent

        up (DirectoryInfo AppContext.BaseDirectory)

    /// The prescribing corpus: every root a worker's recipe is copied from. Both skill roots, because
    /// ADR-0011/0014 keep them byte-identical and a worker reads whichever its harness mounts.
    ///
    /// A MISSING root is a FAILURE, not an empty contribution. Skipping one silently — the obvious
    /// `List.filter Directory.Exists` — would mean a rename quietly halves this gate's subject while the
    /// suite stays green, which is the exact shape (#266) the gate exists to catch. If a root moves, this
    /// says so and the list gets edited.
    let private corpus () =
        [ ".claude/skills"; ".agents/skills"; "docs/coordination" ]
        |> List.map (fun rel ->
            let d = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar))

            if not (Directory.Exists d) then
                failwith
                    $"DocumentedInvocationTests: the prescribing root '%s{rel}' does not exist under %s{repoRoot}. If it moved, update this list — do not let the gate check a smaller corpus than it claims to."

            d)
        |> List.collect (fun d -> Directory.EnumerateFiles(d, "*.md", SearchOption.AllDirectories) |> List.ofSeq)
        |> List.sort

    /// `fsgg-coord <verb>`, but NOT `fsgg-coord-engine <verb>` — the lookahead is load-bearing. `\b`
    /// alone matches between "fsgg-coord" and "-engine", which would read `whoami --mint`'s own
    /// documented line as the verb `engine` and red this gate on a line that is correct.
    let private invocation = Regex(@"(?:scripts/)?fsgg-coord(?![\w-])\s+(?=\S)", RegexOptions.Compiled)

    /// Tokens that end a command line rather than belonging to it. A prescribed line is prose-adjacent:
    /// it carries trailing `# comments`, and it is sometimes piped or chained.
    ///
    /// `#` alone ends a comment; `#889` does NOT — a bare issue ref is a legitimate first argument, and
    /// truncating on any leading `#` would silently drop the ref and check a line nobody wrote.
    let private stops =
        set [ "#"; "|"; "||"; "&&"; ";"; ">"; ">>"; "2>&1"; "\\"; "}"; "{" ]

    /// A shell-lite splitter: quotes group, and adjacent runs concatenate (`--to'x'` is one token), which
    /// is what a real shell does and what the corpus's quoted messages need.
    let private tokenize (s: string) : string list =
        let out = ResizeArray<string>()
        let cur = Text.StringBuilder()
        let mutable started = false
        let mutable quote = '\000'
        let mutable i = 0

        let flush () =
            if started then
                out.Add(cur.ToString())
                cur.Clear() |> ignore
                started <- false

        while i < s.Length do
            let c = s.[i]

            if quote <> '\000' then
                if c = quote then quote <- '\000' else cur.Append c |> ignore
                started <- true
            elif c = '\'' || c = '"' then
                quote <- c
                started <- true
            elif Char.IsWhiteSpace c then
                flush ()
            else
                cur.Append c |> ignore
                started <- true

            i <- i + 1

        flush ()
        List.ofSeq out

    /// Scan for `c`, ignoring one inside quotes. A command substitution closes on a `)` the shell would
    /// see, not on one inside `'…'`.
    let private indexOfUnquoted (s: string) (c: char) : int option =
        let mutable quote = '\000'
        let mutable found = -1
        let mutable i = 0

        while found < 0 && i < s.Length do
            let ch = s.[i]

            if quote <> '\000' then
                (if ch = quote then quote <- '\000')
            elif ch = '\'' || ch = '"' then
                quote <- ch
            elif ch = c then
                found <- i

            i <- i + 1

        if found < 0 then None else Some found

    /// Documentation is written in METAVARIABLES, and the parser validates VALUES. `--pr <pr>` can never
    /// satisfy "a positive PR number", so a gate that fed the placeholder through verbatim would red on
    /// every correct line in the corpus and prove only that `<pr>` is not an integer.
    ///
    /// So a placeholder becomes a value, and synopsis punctuation is dropped:
    ///
    ///   * `<n>` / `<pr>` / `<issue>`  → `1`, which every metavariable position in the corpus accepts.
    ///   * `[--warn]` / `[--repo NAME]` → the brackets go; the flag is checked as if given.
    ///
    /// What survives is the SHAPE — the verb, its flags, and their arity — which is the thing #919 says
    /// nothing checks. A partial placeholder (`FS-GG/<repo>`) is left alone: it is already a legal value.
    let private normalizeToken (t: string) =
        let unbracketed = t.TrimStart('[').TrimEnd(']')

        if unbracketed.StartsWith "<" && unbracketed.EndsWith ">" && unbracketed.Length > 2 then
            "1"
        else
            unbracketed

    /// One command, reduced to the argv a shell would hand the engine. `before` is the text preceding
    /// the invocation on its line, which is what says where the command ENDS.
    let private argvOf (before: string) (rest: string) : string list =
        let segment =
            // `hits="$(scripts/fsgg-coord issues <target> --state all)"` — a command substitution ends at
            // its `)`. Without this the closing `)"` joins the last token and `--state all)"` is refused
            // for a reason nobody wrote.
            if before.TrimEnd().EndsWith "$(" then
                match indexOfUnquoted rest ')' with
                | Some j -> rest.Substring(0, j)
                | None -> rest
            else
                rest

        tokenize segment
        |> List.takeWhile (fun t -> not (stops.Contains t))
        |> List.map normalizeToken

    /// Strip a markdown blockquote marker, so a fence inside a `>` box is still a fence.
    ///
    /// This is load-bearing and was a live fail-open in this gate's first draft. The corpus's WARNING
    /// BOXES are blockquotes — 22 fence markers sit inside them — and a box is precisely where a
    /// hard-won lesson gets written down ("do it THIS way, here is the command"). Without this, a
    /// blockquoted fence never toggles `inFence`, its contents are read as prose, a bare invocation in
    /// one carries no backticks to be found as an inline span, and the gate skips it in SILENCE. The
    /// most carefully-documented invocation in the corpus would be the one nothing checks.
    let private unquote (line: string) =
        let t = line.TrimStart()
        if t.StartsWith ">" then t.Substring(1).TrimStart() else line

    /// A fence opens/closes on ``` — nothing else on the line matters to us.
    let private isFence (line: string) = (unquote line).TrimStart().StartsWith "```"

    /// An inline code span. A prose sentence that NAMES the tool is not prescribing an invocation; a
    /// span that spells one out is. `pnext-item:318` prescribes `say` exactly this way — inline, mid
    /// sentence — so a gate that reads fences only would miss the very line #919 is about.
    let private inlineSpan = Regex(@"`([^`]+)`", RegexOptions.Compiled)

    /// Every invocation the corpus PRESCRIBES, as (file, line number, argv).
    ///
    /// Only code counts — a fenced block, or an inline span. Prose that mentions the tool ("`next`/`take`
    /// read the board to decide…") is discussion, not instruction, and feeding it to the parser would
    /// red this gate on English rather than on a defect.
    let private prescribed () =
        [ for file in corpus () do
              let rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/')
              let mutable inFence = false

              for (i, raw) in File.ReadAllLines file |> Array.indexed do
                  let line = unquote raw

                  if isFence raw then
                      inFence <- not inFence
                  else
                      // A ```console block echoes the prompt. The command is what follows it.
                      let code =
                          if inFence then
                              let t = line.TrimStart()
                              // A whole-line `# comment` inside a fence is prose that happens to live in
                              // code, and it name-drops the tool in backticks exactly as prose does
                              // (`docs/coordination/README.md:52`). Reading it as an invocation checks a
                              // sentence.
                              if t.StartsWith "#" then []
                              elif t.StartsWith "$ " then [ t.Substring 2 ]
                              else [ line ]
                          else
                              [ for m in inlineSpan.Matches line -> m.Groups.[1].Value ]

                      for c in code do
                          for m in invocation.Matches c do
                              let before = c.Substring(0, m.Index)
                              let rest = c.Substring(m.Index + m.Length)

                              match argvOf before rest with
                              | [] -> ()
                              | argv -> yield rel, i + 1, argv ]

    [<Fact>]
    let ``the corpus actually prescribes invocations — this gate has a subject`` () =
        // #266's rule, aimed at this gate: a gate that reports green over an empty subject reports
        // nothing at all. If a rename empties the corpus, THIS fails rather than the suite passing
        // vacuously while nothing is checked.
        let found = prescribed ()
        Assert.NotEmpty found

        let verbs = found |> List.map (fun (_, _, argv) -> List.head argv) |> List.distinct
        Assert.True(List.length verbs >= 10, $"expected the corpus to prescribe many verbs, found: %A{verbs}")

        // The line this whole issue is about must be IN the subject — not merely "some say line".
        // Without this, a regex that silently stops matching inline code still passes every assertion.
        let sayLines =
            found
            |> List.filter (fun (_, _, argv) -> List.head argv = "say")

        Assert.NotEmpty sayLines

    /// KNOWN GAPS — a documented invocation the engine refuses TODAY, with the issue that will close it.
    ///
    /// An exemption list is the fail-open this gate exists to prevent, so this one is **self-retiring**:
    /// the test below fails when a listed gap starts PARSING, and names the entry to delete. The list can
    /// therefore rot in only one direction — toward being deleted — and a fix cannot silently leave a
    /// stale exemption behind that would mask the next regression on the same flag.
    ///
    /// That is `CommandSurfaceTests`' bargain: the point is not to freeze anything, it is to make the
    /// state of the surface cost a line in a diff. An entry here is a DECISION, reviewable, with a number
    /// on it — not a shrug.
    ///
    /// The bar for adding one is high. It is NOT "this line is inconvenient to fix". It is: the corpus
    /// documents behaviour that genuinely shipped, restoring it is a real change with its own story, and
    /// the issue to do it exists.
    let private knownGaps: (string list * string) list =
        [
          // EMPTY, and that is the healthy state. An entry here is a documented invocation the engine
          // REFUSES — a temporary exemption keyed to the issue that will close it. #959 (`who --local`) was
          // the last one, ported and its exemption deleted by the same PR (the `must be deleted, not left to
          // rot` test below reds if a fixed entry lingers). The family before it: #861 (`add`), #867
          // (`release --status`), #919 (`say`). Add an entry ONLY with the issue that retires it.
          ]

    [<Fact>]
    let ``every invocation the corpus prescribes is one the parser ACCEPTS`` () =
        let gapped argv =
            knownGaps |> List.exists (fun (shape, _) -> shape = argv)

        let failures =
            prescribed ()
            |> List.filter (fun (_, _, argv) -> not (gapped argv))
            |> List.choose (fun (file, line, argv) ->
                match parse argv with
                | Ok _ -> None
                | Error e -> Some $"  %s{file}:%d{line}\n    argv:  %A{argv}\n    engine: %s{e}")

        if not (List.isEmpty failures) then
            let body = String.Join("\n\n", failures)

            failwithf
                "%d prescribed invocation(s) the engine REFUSES.\n\n%s\n\nEvery worker's recipe is copied from these files, into a context that is never refreshed. A line\nhere the parser rejects is an instruction the protocol GIVES and the tool REFUSES — #861 (`add`,\nverb absent), #867 (`release --status`, flag unread), #919 (`say`, arguments re-typed), #959\n(`who --local`, flag dropped).\n\nFix the ENGINE if the corpus documents behaviour that shipped — that is the usual answer, and it\nis what #919 decided for this family. Fix the CORPUS only if the line was never true. If neither\nis this PR's story, add a `knownGaps` entry WITH the issue that will close it."
                (List.length failures)
                body

    [<Fact>]
    let ``a known gap that has been FIXED must be deleted, not left to rot`` () =
        // The half that makes `knownGaps` honest. Without it, an exemption outlives its defect and
        // silently re-opens the hole: the next regression on `who --local` would land green, exempted by
        // a line nobody remembered was there. A gate whose allowlist cannot expire is #266's shape
        // wearing this gate's clothes.
        let stale =
            knownGaps
            |> List.choose (fun (argv, why) ->
                match parse argv with
                | Ok _ -> Some $"  %A{argv}\n    listed as: %s{why}"
                | Error _ -> None)

        if not (List.isEmpty stale) then
            failwithf
                "%d knownGaps entry/entries now PARSE — the defect is fixed, so the exemption must go.\n\n%s\n\nDelete the entry from `knownGaps` in this file. Leaving it would exempt a line that no longer\nneeds exempting, and mask the next regression on it."
                (List.length stale)
                (String.Join("\n\n", stale))

    [<Fact>]
    let ``say accepts the form all seven prescribing sites document`` () =
        // The #919 regression, pinned by SHAPE rather than by the corpus — so deleting the doc line
        // cannot make this pass.
        let o = parse [ "say"; ".github#889"; "--to"; "brant-0666"; "I need src/Audio; can you land first?" ]

        match o with
        | Error e -> failwithf "the documented form was refused: %s" e
        | Ok o ->
            Assert.Equal<string list>([ ".github#889" ], o.Args)
            Assert.Equal(Some "brant-0666", o.ToWorker)
            Assert.Equal(Some "I need src/Audio; can you land first?", o.Message)

    [<Fact>]
    let ``say without --to is the broadcast parallel-work prescribes`` () =
        match parse [ "say"; ".github#889"; "Anyone else in here?" ] with
        | Error e -> failwithf "the documented broadcast was refused: %s" e
        | Ok o ->
            Assert.Equal<string list>([ ".github#889" ], o.Args)
            // `*` — anyone holding the item — is what bash defaulted to, and `Client.say` already
            // implements the target. The port simply had no way to ASK for it.
            Assert.Equal(Some "*", o.ToWorker)
            Assert.Equal(Some "Anyone else in here?", o.Message)

    [<Fact>]
    let ``--message remains an explicit alias, so neither documented form is broken`` () =
        match parse [ "say"; ".github#889"; "--to"; "brant-0666"; "--message"; "hi" ] with
        | Error e -> failwithf "the --message form was refused: %s" e
        | Ok o ->
            Assert.Equal<string list>([ ".github#889" ], o.Args)
            Assert.Equal(Some "hi", o.Message)

    [<Fact>]
    let ``an unquoted multi-word message keeps its words`` () =
        // bash joined the trailing positionals, so `say #1 hello world` said "hello world". Dropping
        // "world" would be a silent truncation of the thing the worker is trying to communicate.
        match parse [ "say"; ".github#889"; "hello"; "world" ] with
        | Error e -> failwithf "refused: %s" e
        | Ok o -> Assert.Equal(Some "hello world", o.Message)

    [<Fact>]
    let ``a message given BOTH ways is refused, not silently preferred`` () =
        // The residue rule (`OptionsTests`): picking one and dropping the other tells the caller, by a
        // green exit, that it said something it did not.
        match parse [ "say"; ".github#889"; "--message"; "a"; "b" ] with
        | Ok o -> failwithf "expected a refusal, parsed to %A" o
        | Error e -> Assert.Contains("--message", e)

    [<Fact>]
    let ``the say normalization does not leak into other verbs`` () =
        // `set-field <ref> <field> <value>` is THREE positionals. If `normalizeSay` ever stopped
        // checking the command, it would eat two of them into a message and `set-field` would write
        // nothing while reporting success — #867's exact shape, caused by its own fix.
        match parse [ "set-field"; ".github#889"; "Status"; "Ready" ] with
        | Error e -> failwithf "refused: %s" e
        | Ok o ->
            Assert.Equal<string list>([ ".github#889"; "Status"; "Ready" ], o.Args)
            Assert.Equal(None, o.Message)
            Assert.Equal(None, o.ToWorker)
