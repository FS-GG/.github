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
                let tokens =
                    raw.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.map (fun t -> t.TrimStart('.', '/') |> fun s -> if s = "" then t.Trim() else s)
                    |> List.filter (fun t -> t <> "")
                    |> List.distinct

                match tokens with
                | [] -> Undeclared
                | ts -> Declared(ts |> List.map classify)

    let unmatchable (touchSet: TouchSet) : string list =
        match touchSet with
        | Undeclared
        | DeclaredNone -> []
        | Declared tokens ->
            tokens
            |> List.choose (function
                | Unmatchable t -> Some t
                | Matchable _ -> None)

    let tokensOverlap (a: string) (b: string) : bool =
        let stem (t: string) =
            if t.EndsWith("/**", StringComparison.Ordinal) then t.Substring(0, t.Length - 3)
            elif t.EndsWith("/*", StringComparison.Ordinal) then t.Substring(0, t.Length - 2)
            else t.TrimEnd('/')

        let x = stem a
        let y = stem b

        // Exact, or one contains the other as a SUBTREE. `src/Scene` vs `src/SceneGraph` must NOT
        // overlap — hence the `/` — while `src/Scene` vs `src/Scene/Types.fs` must.
        x = y
        || y.StartsWith(x + "/", StringComparison.Ordinal)
        || x.StartsWith(y + "/", StringComparison.Ordinal)

    let conflicts (a: TouchSet) (b: TouchSet) : (string * string) list =
        let tokensOf =
            function
            | Undeclared
            | DeclaredNone -> []
            | Declared ts ->
                ts
                |> List.map (function
                    | Matchable t -> t
                    | Unmatchable t -> t)

        [ for x in tokensOf a do
              for y in tokensOf b do
                  if tokensOverlap x y then
                      yield (x, y) ]
