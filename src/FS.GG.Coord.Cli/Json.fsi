namespace FS.GG.Coord.Cli

/// The reading half of every wire contract this engine speaks — and the one rule they all obey.
///
/// THE CODEC MAY NOT FAIL OPEN. Every field is required unless its absence is itself a modelled fact,
/// and malformed input is an ERROR, never a default. A defaulted field silently changes the verdict,
/// and the engine would then report agreement — or disagreement — that was manufactured HERE rather
/// than found in the domain. That is epic #266's shape, hiding in a JSON reader.
///
/// `jq -r .foo` over a missing key yields the string `"null"`, and bash then compares it to something.
/// That is how "I could not read it" and "it is not set" became the same value in the first place. None
/// of the accessors below can express it.
module Json =

    open System.Text.Json

    /// Where the document is malformed, and how. Carries a JSON path, so a fleet-wide failure is
    /// diagnosable from the log alone rather than by re-running it.
    type Error =
        { Path: string
          Message: string }

    val err: path: string -> message: string -> Result<'a, Error list>

    /// A REQUIRED field. Absent is an error.
    val prop: path: string -> name: string -> el: JsonElement -> Result<JsonElement, Error list>

    /// A field that may be legitimately absent OR explicitly null — both mean "not set", which is a
    /// FACT here (no claim, no limit), not a failure to read one.
    val optProp: name: string -> el: JsonElement -> JsonElement option

    val asString: path: string -> el: JsonElement -> Result<string, Error list>
    val asInt: path: string -> el: JsonElement -> Result<int, Error list>
    val asBool: path: string -> el: JsonElement -> Result<bool, Error list>
    val asArray: path: string -> el: JsonElement -> Result<JsonElement list, Error list>

    val stringField: path: string -> name: string -> el: JsonElement -> Result<string, Error list>
    val intField: path: string -> name: string -> el: JsonElement -> Result<int, Error list>

    /// Collect EVERY error, not just the first. A wire format that has to be debugged one field per
    /// round-trip, across six repos, does not get debugged.
    val collect: results: Result<'a, Error list> list -> Result<'a list, Error list>
