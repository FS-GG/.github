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
