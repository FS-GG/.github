module FS.GG.Coord.Core.Tests.KitTests

open Xunit
open FS.GG.Coord

// The lock the fixtures write: a comment header, then one `<digest>  <src>` line per kit source.
let private lock =
    "# registry/repos.lock — GENERATED.\n\
     aaaa  .claude/skills/pnext-item\n\
     bbbb  scripts/fsgg-coord\n"

[<Fact>]
let ``parseLock skips the comment header and blank lines, keeping digest+src pairs`` () =
    Assert.Equal<(string * string) list>(
        [ "aaaa", ".claude/skills/pnext-item"; "bbbb", "scripts/fsgg-coord" ],
        Kit.parseLock (lock + "\n   \n")
    )

[<Fact>]
let ``parseLock drops a line that carries a digest but no source field`` () =
    // `read -r want src` with an empty src is `[ -n "$src" ] || continue` — not an entry.
    Assert.Equal<(string * string) list>([ "aaaa", "src/a" ], Kit.parseLock "aaaa  src/a\nbbbb\n")

[<Fact>]
let ``staleSources names exactly the source whose actual digest differs from the lock`` () =
    // scripts/fsgg-coord was edited (its digest is now cccc, not the pinned bbbb); the skill is untouched.
    let resolve =
        function
        | ".claude/skills/pnext-item" -> Some "aaaa"
        | "scripts/fsgg-coord" -> Some "cccc"
        | _ -> None

    Assert.Equal<string list>([ "scripts/fsgg-coord" ], Kit.staleSources resolve (Kit.parseLock lock))

[<Fact>]
let ``staleSources is silent when every digest matches`` () =
    let resolve =
        function
        | ".claude/skills/pnext-item" -> Some "aaaa"
        | "scripts/fsgg-coord" -> Some "bbbb"
        | _ -> None

    Assert.Equal<string list>([], Kit.staleSources resolve (Kit.parseLock lock))

[<Fact>]
let ``staleSources skips an entry whose file this tree does not carry - not a staleness`` () =
    // A receiver mirrors the kit but not `scripts/fsgg-coord`: absent → None → never nagged (#469).
    let resolve =
        function
        | ".claude/skills/pnext-item" -> Some "aaaa"
        | _ -> None

    Assert.Equal<string list>([], Kit.staleSources resolve (Kit.parseLock lock))

[<Fact>]
let ``divergedRoots names a skill whose two roots differ, and one whose mirror is missing`` () =
    let roots =
        [ "same", Some [| 1uy; 2uy |], Some [| 1uy; 2uy |] // byte-identical → clean
          "diverged", Some [| 1uy |], Some [| 9uy |] // edited one root only
          "no-mirror", Some [| 1uy |], None ] // the .agents root is missing the file

    Assert.Equal<string list>([ "diverged"; "no-mirror" ], Kit.divergedRoots roots)
