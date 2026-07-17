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

// ---- skillMirror: the roots rule is scoped to what the REGISTRY declares (#647) --------------------
//
// The scan this replaces globbed `.claude/skills/*/` and byte-compared every directory it found, so it
// policed skills the kit does not own — 33 in FS.GG.Rendering, 28 in FS.GG.SDD, on green `main`s — and
// told the worker to `cp` away per-agent wrapper lines the specs REQUIRE. The lock is the roster; a
// repo-local skill is not in it, and therefore cannot reach these functions at all.

let private lane = [ ".claude/skills"; ".agents/skills" ]

[<Fact>]
let ``skillMirror pairs a declared skill source with its opposite root`` () =
    Assert.Equal(Some(".claude/skills/pnext-item", ".agents/skills/pnext-item"), Kit.skillMirror lane ".claude/skills/pnext-item")

[<Fact>]
let ``skillMirror mirrors FROM whichever root the registry declared - never a hardcoded direction`` () =
    // THE DESTRUCTIVE HALF OF #647. A repo whose source root is `.agents/` (materialize-skill-roots.fsx
    // fans `.agents/` → `.claude/`/`.codex/`) got `cp .claude/… .agents/…` — the mirror run BACKWARDS,
    // over the source. Direction follows the declaration, so the source is never the destination.
    let src, mirror = Kit.skillMirror lane ".agents/skills/pnext-item" |> Option.get
    Assert.Equal(".agents/skills/pnext-item", src)
    Assert.Equal(".claude/skills/pnext-item", mirror)
    Assert.NotEqual<string>(src, mirror)

[<Fact>]
let ``skillMirror ROUND-TRIPS - the emitted remedy reproduces the mirror, not the source`` () =
    // #555's unmet test ask. `assert_contains ".agents/skills/pnext-item"` passed for the BROKEN string
    // too, because a substring check cannot tell a source from a destination. Assert the PAIR, in order.
    for declared in [ ".claude/skills/check-board"; ".agents/skills/check-board" ] do
        let src, mirror = Kit.skillMirror lane declared |> Option.get
        // the remedy copies the DECLARED source over the other root — `cp <src>/SKILL.md <mirror>/SKILL.md`
        Assert.Equal(declared, src)
        Assert.True(src.EndsWith "/check-board" && mirror.EndsWith "/check-board")
        Assert.Equal(Some(mirror, src), Kit.skillMirror lane mirror) // and the mirror's mirror is the source

[<Fact>]
let ``skillMirror yields nothing for a CLIENT kit - a client has no roots to mirror`` () =
    Assert.Equal(None, Kit.skillMirror lane "scripts/fsgg-coord")

[<Fact>]
let ``skillMirror yields nothing for a source outside the kit lane, or a root itself`` () =
    Assert.Equal(None, Kit.skillMirror lane ".codex/skills/pnext-item") // not a root of this lane
    Assert.Equal(None, Kit.skillMirror lane ".claude/skills") // the root itself, not a skill
    Assert.Equal(None, Kit.skillMirror lane ".claude/skills/nested/deeper") // not a skill directory

[<Fact>]
let ``skillMirror over a LOCK reaches the kit's skills and NOTHING else`` () =
    // The end of #647, stated as data: a lock carrying the kit's roster, in a tree that also holds the
    // repo's own skills. The repo-local ones are absent from the lock, so no glob can drag them in.
    let lockText =
        "# GENERATED.\n\
         aaaa  .claude/skills/pnext-item\n\
         bbbb  .claude/skills/check-board\n\
         cccc  scripts/fsgg-coord\n"

    let governed =
        Kit.parseLock lockText |> List.choose (fun (_w, src) -> Kit.skillMirror lane src)

    Assert.Equal<(string * string) list>(
        [ ".claude/skills/pnext-item", ".agents/skills/pnext-item"
          ".claude/skills/check-board", ".agents/skills/check-board" ],
        governed
    )

// ---- declaredSources: the obligation a worker is about to TAKE ON (#509) ---------------------------
//
// `staleSources` answers *is the lock stale?* off the tree. At CLAIM time nothing is edited yet, so it is
// silent by construction — which is exactly why the obligation went unnamed until a red `main` (#469/#509).
// This answers the other question, the only one a declaration CAN answer: *which kit sources is this worker
// about to edit?*
//
// It is NOT the inference #563 removed. That one asked the declaration whether the obligation was MET and
// fell silent when `registry/repos.yml` was named — a fail-open. This can only ADD a warning; there is no
// input for which it suppresses one.

[<Fact>]
let ``declaredSources names a kit source the touch-set declares EXACTLY`` () =
    Assert.Equal<string list>(
        [ "scripts/fsgg-coord" ],
        Kit.declaredSources (Kit.parseLock lock) [ "scripts/fsgg-coord"; "docs/coordination/README.md" ]
    )

[<Fact>]
let ``declaredSources names a kit source a SUBTREE token reaches - either direction`` () =
    // `.claude/skills/pnext-item/**` names the source below it, and the bare parent `.claude/skills` names
    // it just as effectively — a parent RESERVES the subtree (#309), so it incurs the same obligation. Both
    // directions, because `TouchSet.tokensOverlap` is containment either way and erring toward naming an
    // obligation costs a line of advice, where missing one costs a red `main`.
    let sources (token: string) = Kit.declaredSources (Kit.parseLock lock) [ token ]

    Assert.Equal<string list>([ ".claude/skills/pnext-item" ], sources ".claude/skills/pnext-item/**")
    Assert.Equal<string list>([ ".claude/skills/pnext-item" ], sources ".claude/skills/pnext-item")
    Assert.Equal<string list>([ ".claude/skills/pnext-item" ], sources ".claude/skills")

[<Fact>]
let ``declaredSources is SILENT on a touch-set that names no kit source`` () =
    // The counterweight, and the whole reason this is usable on the hottest command in the org: the ordinary
    // item touches no kit source and must say nothing at all. A warning that fires on every claim is one
    // every worker learns to scroll past, and the next one will be real.
    Assert.Equal<string list>(
        [],
        Kit.declaredSources (Kit.parseLock lock) [ "src/FS.GG.Coord.Cli/Client.fs"; "tests/FS.GG.Coord.Core.Tests" ]
    )

[<Fact>]
let ``declaredSources does not confuse a SIBLING whose name merely shares a prefix`` () =
    // `scripts/fsgg-coord-engine` is not `scripts/fsgg-coord`, and a prefix compare that forgot the
    // separator would say it was — warning about an obligation this worker has not taken on, on a path the
    // repo really does carry (`scripts/fsgg-coord` vs the engine that replaced its innards).
    Assert.Equal<string list>([], Kit.declaredSources (Kit.parseLock lock) [ "scripts/fsgg-coord-engine" ])

[<Fact>]
let ``declaredSources reports every kit source a touch-set reaches, in LOCK order`` () =
    // A touch-set may take on more than one obligation, and the worker needs all of them — naming one and
    // stopping would leave the second to CI, which is the failure this exists to end. Order follows the lock
    // so the advice is stable rather than declaration-order noise.
    Assert.Equal<string list>(
        [ ".claude/skills/pnext-item"; "scripts/fsgg-coord" ],
        Kit.declaredSources (Kit.parseLock lock) [ "scripts/fsgg-coord"; ".claude/skills/pnext-item/SKILL.md" ]
    )

[<Fact>]
let ``declaredSources on an EMPTY declaration names nothing - it cannot invent an obligation`` () =
    // `Undeclared`, `Paths: none`, and an unreadable body all reach here as an empty list. None of them is a
    // licence to warn: an item that declares nothing has taken on nothing.
    Assert.Equal<string list>([], Kit.declaredSources (Kit.parseLock lock) [])
