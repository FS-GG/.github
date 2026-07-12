namespace FS.GG.Coord

module Blockers =

    open Types

    let isResolved (blocker: Blocker) : bool =
        match blocker.State with
        | BlockerClosed
        | BlockerMerged -> true
        | BlockerOpen
        | BlockerUnknown
        | BlockerUnparseable -> false

    let unresolved (blockers: Blocker list) : Blocker list =
        blockers |> List.filter (isResolved >> not)
