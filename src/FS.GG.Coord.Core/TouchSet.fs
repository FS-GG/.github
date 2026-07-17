namespace FS.GG.Coord

module TouchSet =

    open System
    open System.Text.RegularExpressions
    open Types

    /// A `Paths:` line: up to three leading spaces, either case.
    let private declRe = Regex(@"^ {0,3}[Pp]aths:\s*(?<rest>.*)$", RegexOptions.Compiled)

    /// The two reserved touch-set words, NORMALISED — or `None` for a real path token. There are two
    /// (#1103 leg 8): `none` reserves nothing and is UNSCHEDULABLE (an epic/decision), `any` reserves
    /// nothing and IS schedulable (a file-less chore). They are one word apart and one bit apart, and a
    /// caller that treats them as one — `Writes.validate` canonicalising both to `none` — turns a chore
    /// into an epic. So the discriminator is typed, not a bool.
    ///
    /// PUBLIC because the sentinels are part of the GRAMMAR, and the grammar lives here — in one place.
    /// Re-deciding it elsewhere is #485's shape (one question, five implementations, agreeing in none)
    /// reproduced inside its own remedy — the exact thing that file's own comment forbids.
    let sentinelToken (token: string) : string option =
        match token.Trim().ToLowerInvariant() with
        | "none"
        | "any" as s -> Some s
        | _ -> None

    /// A reserved word (either sentinel), so it is NEVER a real path. `Writes.validate` and `classify`
    /// both need "this is not a file to reserve"; `sentinelToken` says WHICH sentinel when that matters.
    let isSentinel (token: string) = (sentinelToken token).IsSome

    /// Lines OUTSIDE any fenced code block. A `Paths:` line inside a fence is a QUOTATION of the
    /// grammar, not a use of it (#277) — and the protocol docs quote it constantly.
    ///
    /// ASKED, NOT DECIDED (#972). This was a private `inFence` toggle over a private `^ {0,3}(```|~~~)`,
    /// and `Writes.rewrite` and `EpicBody.childRefs` each had their own — three answers to one question,
    /// agreeing in none, which is the #485 shape this module's own header calls "reproduced inside its own
    /// remedy". `Markdown` is the one place now; the threshold this used to get wrong is gone.
    let private unfenced (body: string) : string list = Markdown.unfenced body

    let classify (token: string) : PathToken =
        // The sentinel is never a PATH. `parse` answers `DeclaredNone` before it gets here when every
        // token is the sentinel, so a `none` reaching this point sits ALONGSIDE real paths — a
        // contradiction ("I touch nothing" and "I touch src/A"), and a mistake in every reading of it.
        // Refusing it here rather than letting it fall through to `Matchable` is what keeps it from
        // reserving a `none` directory that does not exist: a path-shaped token that matches no file is
        // disjoint from every other worker's touch-set, so the item would schedule AND read as
        // conflict-free with everything (#863, and #273's fail-open through the parser built to end it).
        if isSentinel token then
            Unmatchable token
        else

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
            // An EMPTY declaration (`Paths:` with nothing after it) is an OMISSION, and this case must
            // come FIRST: `List.forall` is vacuously TRUE on `[]`, so folding it into the sentinel test
            // below would answer `DeclaredNone` — reporting a bare `Paths:` as a DECISION nobody made.
            // That is #496's conflation with the two sides swapped, and it would read as deliberate.
            | [] -> Undeclared

            // The sentinel is tested over the TOKEN SET, never over the concatenated string (#863).
            // `isSentinel raw` broke on the second declaration: two bare `Paths: none` lines
            // concatenate to `"none none"`, which is not `"none"`, so the sentinel was LOST and `none`
            // fell through to `classify` — which called it `Matchable`, because it is a perfectly
            // path-shaped token. The epic then went Startable and reserved a `none` directory that does
            // not exist, so it read as disjoint from every other worker. The `Unmatchable` gate could
            // not catch it: `none` IS matchable; it just matches no file that exists, which is the one
            // thing that gate does not test. Deciding over tokens makes the union in the `.fsi`'s
            // "multiple bare declarations UNIONED" promise mean the same thing for one line or for ten.
            //
            // TWO sentinels now (#1103 leg 8), and they must be UNANIMOUS: every token `none` → epic,
            // every token `any` → chore. A MIX (`none any`, or `none src/A`) is a contradiction — the
            // item cannot both reserve nothing and reserve something, nor be both unschedulable and
            // schedulable — so it falls through to `Declared`, where `classify` marks the reserved word
            // `Unmatchable` and the item reports the contradiction as an unusable token, exactly as #863
            // catches `none src/A` today.
            | ts when ts |> List.forall (fun t -> sentinelToken t = Some "none") -> DeclaredNone
            | ts when ts |> List.forall (fun t -> sentinelToken t = Some "any") -> DeclaredChore

            | ts -> Declared(ts |> List.map classify)

    let unmatchable (touchSet: TouchSet) : string list =
        match touchSet with
        | Undeclared
        | DeclaredNone
        // A chore reserves nothing DELIBERATELY, so it names no unmatchable token — the same clean bill
        // `DeclaredNone` gets, and for the same reason: there are no tokens to be unusable.
        | DeclaredChore
        // We never read the body, so we know of NO unmatchable tokens — which is emphatically not the
        // claim that it has none. The scheduler rejects `Unreadable` before it ever asks this (it is
        // `Undetermined`), and the linter must not report a clean bill of health on an unread item.
        | Unreadable _ -> []
        | Declared tokens ->
            tokens
            |> List.choose (function
                | Unmatchable t -> Some t
                | Matchable _ -> None)

    type Usability =
        | Usable
        | AllUnmatchable of tokens: string list
        | SomeUnmatchable of tokens: string list

    /// THE USABILITY RULE (#864, #945). See the .fsi for the threshold and for why the every/some
    /// distinction lives HERE rather than in the one caller that renders it.
    let usability (touchSet: TouchSet) : Usability =
        match unmatchable touchSet, touchSet with
        | [], _ -> Usable
        | bad, Declared tokens when
            tokens
            |> List.exists (function
                | Matchable _ -> true
                | Unmatchable _ -> false)
            ->
            SomeUnmatchable bad
        | bad, _ -> AllUnmatchable bad

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
    let covers (token: PathToken) (file: string) : bool =
        match token with
        // #273: reserves nothing, covers nothing.
        | Unmatchable _ -> false
        | Matchable t ->
            let s = stem t
            file = s || file.StartsWith(s + "/", System.StringComparison.Ordinal)

    let conflicts (a: TouchSet) (b: TouchSet) : (string * string) list =
        let tokensOf =
            function
            | Undeclared
            | DeclaredNone
            // A chore reserves nothing, so it conflicts with nothing — it is compatible with ANY
            // concurrent touch-set, which is the whole meaning of `Paths: any` (#1103 leg 8).
            | DeclaredChore
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
