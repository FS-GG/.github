namespace FS.GG.Coord.GitHub

/// One incident-specific, read-only provider qualification followed by a private append-only no-effect certificate.
/// The caller supplies a read transport; this API never has or calls a mutation sender.
module V2TendFieldRecovery =
    val internal reconstruct: record: System.Text.Json.Nodes.JsonObject -> byte array
    val internal readCurrent:
        reads: Transport.IGitHubTransport -> record: System.Text.Json.Nodes.JsonObject -> Result<string, string>
    val admit:
        reads: Transport.IGitHubTransport -> syncDirectory: (string -> unit) ->
        input: V2TendNoEffect.Input -> Result<string, string>
