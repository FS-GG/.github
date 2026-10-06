namespace FS.GG.Telemetry.Host

open FS.GG.Coord

/// Pure declarations. Receipt admission remains exclusively receiver-owned.
module internal NativeResponsesFacts =
    type Packet =
        { EnvelopeBytes: byte array
          OriginRef: byte array
          ObservationRef: byte array
          UsageRef: byte array option
          InventoryRef: byte array option
          SourceRef: byte array option }

    /// Test-only declaration renderer; neither constructs VerifiedCapture nor grants authority.
    val renderFixture:
        origin: NativeResponsesCollection.OriginEvidence ->
        operation: NativeResponsesCollection.OperationEvidence ->
        observation: NativeResponses.ResponseObservation -> Result<Packet, string list>

    /// Production accepts only the collection owner's opaque verified capture.
    val build: capture: NativeResponsesCollection.VerifiedCapture -> Result<Packet, string list>
