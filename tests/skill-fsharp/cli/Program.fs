open FS.GG.Coord.Cli

[<EntryPoint>]
let main argv =
    match SkillCommandApplication.tryRun (Array.toList argv) with
    | Some code -> code
    | None -> 99
