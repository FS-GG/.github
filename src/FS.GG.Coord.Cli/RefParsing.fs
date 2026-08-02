namespace FS.GG.Coord.Cli

open System
open FS.GG.Coord.Types

module RefParsing =
    let parse (owner: string) (defaultRepo: string option) (raw: string) : Result<Ref, string> =
        let forms = "use a URL, owner/repo#n, repo#n, or a bare n inside the repo's checkout"
        let numbered ownr repo (digits: string) =
            match Int32.TryParse(digits, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture) with
            | true, n -> Ok { Owner = ownr; Repo = repo; Number = n }
            | _ -> Error $"issue number '%s{digits}' is out of range (the largest is %d{Int32.MaxValue})."
        let url = Text.RegularExpressions.Regex.Match(raw, @"github\.com/([\w.-]+)/([\w.-]+)/issues/(\d+)")
        if url.Success then numbered url.Groups.[1].Value url.Groups.[2].Value url.Groups.[3].Value
        else
            let full = Text.RegularExpressions.Regex.Match(raw, @"^([\w.-]+)/([\w.-]+)#(\d+)$")
            if full.Success then numbered full.Groups.[1].Value full.Groups.[2].Value full.Groups.[3].Value
            else
                let short = Text.RegularExpressions.Regex.Match(raw, @"^([\w.-]+)#(\d+)$")
                if short.Success then numbered owner short.Groups.[1].Value short.Groups.[2].Value
                else
                    let bare = Text.RegularExpressions.Regex.Match(raw, @"^#?(\d+)$")
                    if bare.Success then
                        match defaultRepo with
                        | Some repo -> numbered owner repo bare.Groups.[1].Value
                        | None -> Error $"cannot resolve the bare issue number '%s{raw}' — no %s{owner} repo to infer it from (no --repo, and this is not a %s{owner} checkout). Name the repo: use a URL, owner/repo#n, or repo#n."
                    else Error $"unrecognised issue ref '%s{raw}' (%s{forms})."

    type BoardShorthandClose =
        { Matched: string
          Ref: string
          Number: string }

    // `\s+` between the keyword and the ref is load-bearing, not decorative — it is what makes the
    // TWO valid GitHub forms structurally unable to match:
    //   * a bare `#2095`   — `[\w.-]+` demands at least one character before `#`, and a bare ref has
    //     none right after the required whitespace, so the token class never gets started.
    //   * `owner/repo#2095` — `[\w.-]+` excludes `/`, so it can capture at most `repo`'s side, and
    //     the character immediately after that partial capture is `/`, never the `#` the pattern
    //     requires next. Every backtrack shrinks the same way and never reaches `#`.
    // Only the board's OWN `<repo>#<n>` shorthand — a single `[\w.-]+` token, no `/`, immediately
    // followed by `#<n>` — satisfies both constraints at once, which is exactly the one form GitHub
    // will not parse.
    let private boardShorthandCloseDoc =
        Text.RegularExpressions.Regex(
            @"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b\s+([\w.-]+)#(\d+)",
            Text.RegularExpressions.RegexOptions.IgnoreCase
        )

    let boardShorthandCloses (body: string) : BoardShorthandClose list =
        boardShorthandCloseDoc.Matches(body)
        |> Seq.cast<Text.RegularExpressions.Match>
        |> Seq.map (fun m ->
            { Matched = m.Value
              Ref = $"%s{m.Groups.[1].Value}#%s{m.Groups.[2].Value}"
              Number = m.Groups.[2].Value })
        |> List.ofSeq
