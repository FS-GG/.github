namespace FS.GG.Coord

module TouchSet =

    open System
    open System.Text.RegularExpressions
    open Types

    /// A `Paths:` line: up to three leading spaces, either case.
    let private declRe = Regex(@"^ {0,3}[Pp]aths:\s*(?<rest>.*)$", RegexOptions.Compiled)

    /// A fence opens or closes on ``` or ~~~ at up to three leading spaces.
    let private fenceRe = Regex(@"^ {0,3}(```|~~~)", RegexOptions.Compiled)

    /// The sentinel, after normalisation.
    let private isNoneSentinel (s: string) = s.Trim().ToLowerInvariant() = "none"

    /// Lines OUTSIDE any fenced code block. A `Paths:` line inside a fence is a QUOTATION of the
    /// grammar, not a use of it (#277) — and the protocol docs quote it constantly.
    let private unfenced (body: string) : string list =
        let lines = body.Replace("\r\n", "\n").Split('\n')

        let mutable inFence = false
        let acc = ResizeArray<string>()

        for line in lines do
            if fenceRe.IsMatch line then inFence <- not inFence
            elif not inFence then acc.Add line

        List.ofSeq acc

    let classify (token: string) : PathToken =
        // Strip the ONE sanctioned wildcard — a TRAILING `/**`, `/*`, or a trailing `/` — then ask
        // whether any glob metacharacter survives. If one does, the token can match no file.
        let stem =
            if token.EndsWith("/**", StringComparison.Ordinal) then token.Substring(0, token.Length - 3)
            elif token.EndsWith("/*", StringComparison.Ordinal) then token.Substring(0, token.Length - 2)
            elif token.EndsWith("/", StringComparison.Ordinal) then token.Substring(0, token.Length - 1)
            else token

        if stem = "" then Unmatchable token
        elif stem.IndexOfAny([| '*'; '?'; '['; ']' |]) >= 0 then Unmatchable token
        else Matchable token

    let parse (body: string) : TouchSet =
        let declarations =
            unfenced body
            |> List.choose (fun line ->
                let m = declRe.Match line
                if m.Success then Some(m.Groups.["rest"].Value) else None)

        match declarations with
        | [] -> Undeclared
        | ds ->
            let raw =
                ds
                |> List.map (fun d -> d.Replace("`", "").Replace(",", " "))
                |> String.concat " "

            // A declaration whose only content is the sentinel is a DECISION, not an omission (#496).
            if isNoneSentinel raw then
                DeclaredNone
            else
                // A LEADING `./` — AND NOTHING MORE.
                //
                // This was `TrimStart('.', '/')`, which strips EVERY leading dot and slash. It was
                // written to normalise `./src/foo` and it also ate the leading dot of every DOTFILE
                // path: `.github/workflows/**` became `github/workflows/**`, `.agents/skills/` became
                // `agents/skills/`. In this org that is most of the fabric — `.github/`, `.agents/`,
                // `.claude/`, `.config/` — so most touch-sets the engine parsed named directories that
                // do not exist.
                //
                // THE SHADOW CANNOT SEE THIS, AND THAT IS THE INTERESTING PART. It compares OUTCOMES,
                // and a consistent renaming of every token preserves the overlap relation exactly: two
                // items that conflicted still conflict, two that did not still do not. So both engines
                // agree on every verdict, the divergence log stays clean, and the parse is wrong the
                // whole time. A differential test is blind to an error its two sides make identically.
                //
                // It becomes a REAL fail-open at the flip, when the engine's tokens are the ones that
                // meet actual file paths: `.github/workflows/x.yml` (the file) would not match
                // `github/workflows/**` (the token), so the touch-set would reserve NOTHING — and a
                // token that matches no file conflicts with nothing, which is #273's lock succeeding
                // under exactly the conditions it exists to prevent.
                //
                // `sed -E 's#^\./##'` is what the bash client does. This is that, and only that.
                let stripDotSlash (t: string) =
                    if t.StartsWith "./" then t.Substring 2 else t

                let tokens =
                    raw.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.map (fun t -> t.Trim() |> stripDotSlash)
                    |> List.filter (fun t -> t <> "")
                    |> List.distinct

                match tokens with
                | [] -> Undeclared
                | ts -> Declared(ts |> List.map classify)

    let unmatchable (touchSet: TouchSet) : string list =
        match touchSet with
        | Undeclared
        | DeclaredNone
        // We never read the body, so we know of NO unmatchable tokens — which is emphatically not the
        // claim that it has none. The scheduler rejects `Unreadable` before it ever asks this (it is
        // `Undetermined`), and the linter must not report a clean bill of health on an unread item.
        | Unreadable _ -> []
        | Declared tokens ->
            tokens
            |> List.choose (function
                | Unmatchable t -> Some t
                | Matchable _ -> None)

    /// The token with its trailing `/**` or `/*` taken off — the SUBTREE it actually names.
    ///
    /// This is the form a collision is REPORTED in, not just the form it is matched in. `src/Off/Sub/**`
    /// and `src/Off/Sub` name the same subtree, and printing the raw suffix beside a reservation that has
    /// none reads as though the two tokens were different things. Bash has always reported the stem; the
    /// engine must, or the flip silently rewords every collision line a worker has ever seen.
    let stem (t: string) =
        if t.EndsWith("/**", StringComparison.Ordinal) then t.Substring(0, t.Length - 3)
        elif t.EndsWith("/*", StringComparison.Ordinal) then t.Substring(0, t.Length - 2)
        else t.TrimEnd('/')

    let tokensOverlap (a: string) (b: string) : bool =
        let x = stem a
        let y = stem b

        // Exact, or one contains the other as a SUBTREE. `src/Scene` vs `src/SceneGraph` must NOT
        // overlap — hence the `/` — while `src/Scene` vs `src/Scene/Types.fs` must.
        x = y
        || y.StartsWith(x + "/", StringComparison.Ordinal)
        || x.StartsWith(y + "/", StringComparison.Ordinal)

    /// A touch-set we could not read yields NO tokens — so it would COLLIDE WITH NOTHING, and every
    /// candidate would clear it. That is a fail-open, and it is why `Unreadable` may never reach here as
    /// a RESERVATION: `Batch.schedule` refuses the whole batch on one (see `unusableReservation`), and
    /// the scheduler rejects it as a CANDIDATE before disjointness is ever asked. This function is total
    /// because the type demands it, not because an unreadable surface is safe to compare.
    let conflicts (a: TouchSet) (b: TouchSet) : (string * string) list =
        let tokensOf =
            function
            | Undeclared
            | DeclaredNone
            | Unreadable _ -> []
            | Declared ts ->
                ts
                |> List.map (function
                    | Matchable t -> t
                    | Unmatchable t -> t)

        [ for x in tokensOf a do
              for y in tokensOf b do
                  if tokensOverlap x y then
                      yield (x, y) ]
