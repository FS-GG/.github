namespace FS.GG.Coord.Cli

module RefParsing =
    open FS.GG.Coord.Types
    val parse: owner: string -> defaultRepo: string option -> raw: string -> Result<Ref, string>
