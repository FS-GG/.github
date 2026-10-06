namespace FS.GG.Coord

open System.Text.Json

/// Generated closed shape validators. Producer authority, resource counters, reference
/// freshness and native eligibility are checked separately in the canonical store.
module EfficiencyInput =
    type Record
    val parseEvent: JsonElement -> Result<Record, string>
    val validateCommand: action: string -> JsonElement -> Result<unit, string>
    val validateReference: JsonElement -> Result<unit, string>
    val body: Record -> JsonElement
