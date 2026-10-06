namespace FS.GG.Coord

open System.Text.Json

/// The private evidence packet codec. Canonical source and authority joins remain store-owned.
module EfficiencyEvidence =
    /// Map supported canonical facts to the analyst semantic reference vocabulary.
    val semanticKind: kind: string -> string option
    /// Encode integer-valued JSON using the retained Python compact sorted ASCII codec.
    val encode: node: JsonElement -> Result<byte array, string>
    /// Validate retained bytes and codec identity; this does not authenticate their references.
    val validate: bytes: byte array -> Result<JsonElement, string>
