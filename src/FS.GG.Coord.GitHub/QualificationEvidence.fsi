namespace FS.GG.Coord.GitHub

open FS.GG.Coord

/// Exact-subject GitHub evidence projected into the pure qualification contract.
module QualificationEvidence =
    [<Literal>]
    val HostedSchema: string = "fsgg.qualification.hosted-observation/1"

    [<Literal>]
    val ObligationReadbackSchema: string = "fsgg.qualification.obligation-readback/1"

    type HostedScope = WorkflowRun | Job | CheckRun
    type HostedState = Queued | InProgress | Completed of conclusion: string
    type HostedItem =
        { Scope: HostedScope
          Id: string
          HeadSha: string
          State: HostedState }
    type HostedSnapshot =
        { Complete: bool
          Items: HostedItem list }

    type ObligationComment =
        { CommentId: int64
          Url: string
          Author: string
          Body: string }

    val observeHosted: HostedSnapshot -> Qualification.HostedObservation
    val parseHostedSnapshot: bytes: byte array -> Result<HostedSnapshot, string list>
    val renderObligationComment: headSha: string -> Qualification.ObligationDeclaration -> string
    val parseObligationReadback: bytes: byte array -> Result<ObligationComment, string list>
