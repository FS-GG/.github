namespace FS.GG.Coord.Cli

open FS.GG.Coord
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub

/// Pure application service for lint verdicts.
///
/// Transport reads and CLI presentation stay at the composition edge; this module owns the
/// deterministic decisions between them.
module LintApplication =

    type EpicFinding =
        { Code: string
          Severity: string
          Detail: string }

    type Summary =
        { Errors: int
          Notes: int
          Fails: bool }

    val badTouchSetDetail: status: string -> usability: TouchSet.Usability -> string option

    val epicVerdict:
        state: IssueState ->
        status: BoardStatus ->
        body: string ->
        graph: Reads.SubIssueSet ->
        unlinked: string list ->
            EpicFinding list

    val summarize: strict: bool -> severities: string list -> Summary
