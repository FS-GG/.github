namespace FS.GG.Coord.GitHub

/// Closed recovery for the three invalid option-scalar requests in the 2026-11-02 incident (#5206).
/// Certificates acknowledge no execution; they never assert a target was written or authorize a resend.
module V2TendNoEffect =
    open System
    open System.Text.Json.Nodes

    type Input =
        { CustodyRoot: string
          OriginalPath: string
          RequestPath: string
          OriginalRuntimeDirectory: string
          RootAdmissionReference: string }

    type internal Policy =
        { Run: string
          Attempts: Map<string, string>
          Assemblies: Map<string, string> }

    type internal Admission =
        { Acquire: unit -> IDisposable
          ReadCurrent: JsonObject -> Result<string, string>
          ReadSchema: unit -> Result<string, string>
          Reconstruct: JsonObject -> byte array
          SyncDirectory: string -> unit
          Clock: TimeProvider }

    val internal policy: Policy
    val internal digest: bytes: byte array -> string
    val internal isIncident: path: string -> bool
    val internal settled: root: string -> path: string -> bool
    val internal settledWith: p: Policy -> root: string -> path: string -> bool
    val internal admitWith: p: Policy -> deps: Admission -> input: Input -> Result<string, string>
