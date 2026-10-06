namespace FS.GG.Coord

open System.Text.Json

/// Shape validation is separate from canonical admission and native measured resources.
module EfficiencyInput =
    type Record
    val parseEvent: JsonElement -> Result<Record, string>
    val validateCommand: action: string -> JsonElement -> Result<unit, string>
    val validateReference: JsonElement -> Result<unit, string>
    val body: Record -> JsonElement
