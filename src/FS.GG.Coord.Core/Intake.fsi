namespace FS.GG.Coord

/// Versioned, side-effect-free intake draft validation.
module Intake =
    [<Literal>]
    val Schema: string = "fsgg.coord.intake/v1"

    type Disposition = Create | Reuse

    type Draft =
        { Schema: string
          Id: string
          Owner: string
          Repository: string
          Title: string
          Observed: string
          RootCause: string
          Acceptance: string
          Verification: string
          Paths: string list
          Class: string
          Status: string
          Disposition: Disposition option }

    type Finding = { Field: string; Detail: string }

    /// Validate only intrinsic draft facts. Live ownership, duplicate and board facts belong to the IO layer.
    val validate: Draft -> Result<Draft, Finding list>
