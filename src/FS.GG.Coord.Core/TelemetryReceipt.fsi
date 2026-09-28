namespace FS.GG.Coord

module TelemetryReceipt =
    [<Literal>]
    val Schema: string = "fsgg.telemetry.envelope/1"

    [<Literal>]
    val MaxEnvelopeBytes: int = 73728
    [<Literal>]
    val MaxAdmissionBytes: int = 77824

    type Scope =
        {
            Workspace: string
            Producer: string
            Stream: string
        }

    type ProducerRole =
        | Generic
        | NativeCollector

    type Principal =
        {
            Scope: Scope
            Role: ProducerRole
            GrantId: string option
            GrantGeneration: int64 option
        }

    type Envelope =
        {
            Scope: Scope
            BatchId: string
            Batch: TelemetryStore.Batch
            Canonical: string
            Digest: string
            Key: string
        }

    type Admission =
        {
            Envelope: Envelope
            Principal: Principal
            Canonical: string
        }

    val validId: value: string -> bool
    val key: producer: string -> batch: string -> string
    val parse: bytes: byte array -> Result<Envelope, string list>
    val authorize: scope: Scope -> envelope: Envelope -> Result<unit, string list>
    val genericPrincipal: scope: Scope -> Principal
    val validPrincipal: principal: Principal -> bool
    val encodeAdmission: principal: Principal -> envelope: Envelope -> Result<string, string list>
    val parseAdmission: bytes: byte array -> Result<Admission, string list>
