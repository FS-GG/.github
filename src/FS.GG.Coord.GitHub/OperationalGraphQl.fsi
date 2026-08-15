namespace FS.GG.Coord.GitHub

/// Typed operational reads and mutations used by repository automation. All response envelopes and
/// Relay pagination stay behind `GraphQl`; callers receive only domain records.
module OperationalGraphQl =
    open Errors
    open Transport

    type RepositoryPolicy =
        { IssueCreationPolicy: string
          HasIssuesEnabled: bool }

    type ArchiveRow =
        { ItemId: string
          Status: string option
          BlockedBy: string option
          Number: int option
          State: string option
          ClosedAt: string option
          Repo: string option }

    type ArchiveScan =
        { Items: ArchiveRow list
          Pages: int
          Spent: int }

    type RosterRow =
        { Owner: string
          Repo: string
          Number: int option
          Status: string }

    val projectVisibility: IGitHubTransport -> owner: string -> title: string -> IoResult<bool option>
    val projectId: IGitHubTransport -> owner: string -> number: int -> IoResult<string>
    val repositoryPolicy: IGitHubTransport -> owner: string -> name: string -> IoResult<RepositoryPolicy>
    val meterRemaining: IGitHubTransport -> IoResult<int>
    val archiveScan: IGitHubTransport -> projectId: string -> IoResult<ArchiveScan>
    val archiveItems: IGitHubTransport -> projectId: string -> itemIds: string list -> IoResult<unit>
    val rosterBoard: IGitHubTransport -> owner: string -> title: string -> IoResult<RosterRow list>
