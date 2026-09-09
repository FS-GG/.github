namespace FS.GG.Telemetry

open System
open FS.GG.Coord

module RemoteContract =
    [<Literal>]
    val ReceiptSchema: string = "fsgg.telemetry.receipt/1"
    [<Literal>]
    val ErrorSchema: string = "fsgg.telemetry.error/1"
    type Receipt =
        { Scope: TelemetryReceipt.Scope
          BatchId: string
          Digest: string
          Status: string
          Code: string option }
    type ClientConfig =
        { Endpoint: Uri
          CredentialReference: string }
    val validateClientConfig: ClientConfig -> Result<ClientConfig,string>
    val parseReceipt: byte array -> Result<Receipt,string>
    val writeError: code: string -> byte array
    val parseError: byte array -> string option
    val validErrorCode: string -> bool
