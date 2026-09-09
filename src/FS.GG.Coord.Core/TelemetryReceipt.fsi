namespace FS.GG.Coord

module TelemetryReceipt =
    [<Literal>]
    val Schema: string = "fsgg.telemetry.envelope/1"
    [<Literal>]
    val MaxEnvelopeBytes: int = 73728
    type Scope = { Workspace: string; Producer: string; Stream: string }
    type Envelope =
        { Scope: Scope
          BatchId: string
          Batch: TelemetryStore.Batch
          Canonical: string
          Digest: string
          Key: string }
    val validId: value: string -> bool
    val key: producer: string -> batch: string -> string
    val parse: bytes: byte array -> Result<Envelope, string list>
    val authorize: scope: Scope -> envelope: Envelope -> Result<unit, string list>
