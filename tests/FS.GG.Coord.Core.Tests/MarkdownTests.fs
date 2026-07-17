module FS.GG.Coord.Core.Tests.MarkdownTests

open Xunit
open FS.GG.Coord

// THE ONE FENCE RULE (#972). Every row here is a case on which the three private trackers this module
// replaced disagreed with each other, and none of them had a test — the divergence was invisible to the
// suite, which is why it survived from #277 to #965. #864's cure with #864's discipline: the rule is asked
// in one place, and the cases that used to be answered differently are pinned here.

let private text body =
    Markdown.unfenced body

// ---- what opens and closes a fence ----------------------------------------------------------------

[<Fact>]
let ``a line inside a fence is Code; the markers are structure; everything else is Text`` () =
    let body = "before\n```\nquoted\n```\nafter"

    Assert.Equal<(string * Markdown.LineKind) list>(
        [ "before", Markdown.Text
          "```", Markdown.FenceMarker
          "quoted", Markdown.Code
          "```", Markdown.FenceMarker
          "after", Markdown.Text ],
        Markdown.classify body
    )

[<Fact>]
let ``both spellings open a fence, with an info string and up to three spaces of indent`` () =
    Assert.Equal<string list>([ "after" ], text "```\nquoted\n```\nafter")
    Assert.Equal<string list>([ "after" ], text "~~~\nquoted\n~~~\nafter")
    Assert.Equal<string list>([ "after" ], text "```markdown\nquoted\n```\nafter")
    // The opener may be indented up to three, and the closer need not share its indent.
    Assert.Equal<string list>([ "after" ], text "   ```\nquoted\n  ```\nafter")

[<Fact>]
let ``FOUR spaces is an indented code block, not a fence`` () =
    // `Writes` used `^\s*`, so it called this a fence; `TouchSet` used `^ {0,3}`, so it did not. They read
    // the same body two ways, and `widen` wrote under one rule while `take` scheduled under the other.
    // Three is the CommonMark bound, and the tie is broken in CommonMark's favour because that is what
    // GitHub renders — i.e. what the body's author saw.
    Assert.Equal<string list>([ "    ```"; "still text" ], text "    ```\nstill text")

[<Fact>]
let ``a TAB-indented marker is an indented code block too - the fail-open direction`` () =
    // CommonMark advances a leading tab to the next 4-column tab stop, so this marker is at column 4 and
    // GitHub renders an indented code block containing a literal ```. A fence rule that admitted `\t` would
    // open a fence GitHub does not and swallow the LIVE declaration below it — the item would sit `Ready`,
    // apparently declared, and never schedule. Inventing a fence loses declarations; missing one merely
    // quotes them.
    Assert.Equal<string list>([ "\t```"; "Paths: src/real/**" ], text "\t```\nPaths: src/real/**")

    // A space then a tab still reaches the tab stop at column 4.
    Assert.Equal<string list>([ " \t```"; "Paths: src/real/**" ], text " \t```\nPaths: src/real/**")

// ---- the cases the toggle trackers got wrong -------------------------------------------------------

[<Fact>]
let ``a fence closes only on its OWN character - a backtick block CARRIES a tilde line`` () =
    // Both trackers flipped a bool on any marker, so this block ended at the `~~~` and `Paths:` below it
    // was read as a live declaration — a quoted example becoming a real reservation, which is exactly the
    // mis-scheduling #277 exists to prevent.
    let body = "```\n~~~\nPaths: src/quoted/**\n```\nafter"
    Assert.Equal<string list>([ "after" ], text body)

[<Fact>]
let ``a closer must be at least as long as the opener`` () =
    let body = "````\n```\nPaths: src/quoted/**\n````\nafter"
    Assert.Equal<string list>([ "after" ], text body)

[<Fact>]
let ``a closer carries no info string - a marker with one is content`` () =
    let body = "```\n``` not a closer\nPaths: src/quoted/**\n```\nafter"
    Assert.Equal<string list>([ "after" ], text body)

// ---- the fail-open guard --------------------------------------------------------------------------

[<Fact>]
let ``a backtick run whose info string contains a backtick is NOT a fence`` () =
    // Inventing a fence LOSES declarations; missing one merely quotes them. This is the fail-open
    // direction, so it is the one that gets the extra rule.
    Assert.Equal<string list>([ "```#5``` is the ref"; "Paths: src/real/**" ], text "```#5``` is the ref\nPaths: src/real/**")

[<Fact>]
let ``the tilde spelling has no info-string rule`` () =
    Assert.Equal<string list>([], text "~~~a~b\nquoted")

// ---- unterminated fences --------------------------------------------------------------------------

[<Fact>]
let ``an unterminated fence runs to the end of the body - as GitHub renders it`` () =
    Assert.Equal<string list>([ "real" ], text "real\n```\nquoted forever")

[<Fact>]
let ``unterminatedFenceCloser names the OPENER's marker, or None when the body ends outside a fence`` () =
    // `Writes` appended a literal "```" whatever the opener was, so a `~~~~` fence was "closed" with a
    // marker that closes nothing.
    Assert.Equal(Some "```", Markdown.unterminatedFenceCloser "a\n```\nb")
    Assert.Equal(Some "~~~~", Markdown.unterminatedFenceCloser "a\n~~~~\nb")
    Assert.Equal(Some "`````", Markdown.unterminatedFenceCloser "a\n`````\nb")
    Assert.Equal(None, Markdown.unterminatedFenceCloser "a\n```\nb\n```\nc")
    Assert.Equal(None, Markdown.unterminatedFenceCloser "no fences here")

// ---- the edges every caller used to repeat ---------------------------------------------------------

[<Fact>]
let ``a null or empty body is an empty one - normalisation lives here, not in each caller`` () =
    // Every caller used to do its own `isNull` dance before its own `.Replace("\r\n", "\n").Split('\n')`.
    // It lives here now, so there is one place to get it wrong.
    Assert.Equal<string list>([ "" ], text "")
    Assert.Equal<string list>([ "" ], text null)
    Assert.Equal<(string * Markdown.LineKind) list>([ "", Markdown.Text ], Markdown.classify null)
    Assert.Equal(None, Markdown.unterminatedFenceCloser null)

[<Fact>]
let ``CRLF is normalised before the rule runs`` () =
    Assert.Equal<string list>([ "after" ], text "```\r\nquoted\r\n```\r\nafter")
