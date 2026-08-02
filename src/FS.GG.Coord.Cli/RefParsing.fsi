namespace FS.GG.Coord.Cli

module RefParsing =
    open FS.GG.Coord.Types
    val parse: owner: string -> defaultRepo: string option -> raw: string -> Result<Ref, string>

    /// One place a closing keyword (`close`/`closes`/`closed`/`fix`/`fixes`/`fixed`/`resolve`/
    /// `resolves`/`resolved`, case-insensitive) sits directly next to the board's OWN `<repo>#<n>`
    /// shorthand — exactly `parse`'s `short` form, e.g. `.github#2095`. That is the ONE ref shape
    /// GitHub's closing-keyword grammar does not parse: it wants a bare `#<n>` for a same-repo issue,
    /// or `owner/repo#<n>` for a cross-repo one. Written this way in a PR body, it renders as plain
    /// text — GitHub never links it, the merge never closes the issue, and unlike everywhere else a
    /// closing keyword misfires, there is no repair once the PR has merged: editing the body does not
    /// replay the close (.github#2107).
    type BoardShorthandClose =
        { /// The exact substring matched — the keyword through the issue number — so a reader can find
          /// it in the body without re-deriving the pattern.
          Matched: string
          /// The board-shorthand ref alone, e.g. `.github#2095`, for rendering the corrected forms.
          Ref: string
          /// The bare issue number, e.g. `2095`.
          Number: string }

    /// Every board-shorthand closing keyword in `body`, in the order they appear. Empty when there are
    /// none — including when the body correctly used a bare `#<n>` or an `owner/repo#<n>` instead:
    /// NEITHER of those forms can match this pattern, by construction (a bare ref has no token before
    /// `#` for the pattern to capture, and an `owner/repo#<n>` ref's `/` breaks the token class before
    /// `#` is ever reached).
    val boardShorthandCloses: body: string -> BoardShorthandClose list
