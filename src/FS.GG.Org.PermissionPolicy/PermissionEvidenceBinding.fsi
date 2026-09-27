namespace FS.GG.Org.PermissionPolicy

/// A provider-supplied read of the callee named by one reusable-workflow call.
type CalleeOrigin =
    | WorkingTree
    | ExactRefRead

type CalleeContentFact =
    { Repository: string
      WorkflowPath: string
      Ref: string
      Origin: CalleeOrigin
      Text: string }

type RosterFact =
    { Repository: string
      Path: string
      Repositories: string list }

/// The pinned inventory is a separate fact; its provider read remains external.
type AppGrantFact =
    { Repository: string
      InventoryId: string
      Grants: (string * PermissionLevel) list }

type PermissionBindingFacts =
    { CallerRepository: string
      Roster: RosterFact option
      Callee: CalleeContentFact option
      AppGrants: AppGrantFact option }

type BoundPermissionCall =
    { CallerRepository: string
      Call: ReusableWorkflowCall
      CalleeText: string
      AppGrants: AppGrantFact }

/// Pure identity checks for provider facts. This module does not fetch, authenticate or compare grants.
[<RequireQualifiedAccess>]
module PermissionEvidenceBinding =
    val validateAppGrant: expectedInventoryId: string -> grants: AppGrantFact -> Result<AppGrantFact, string>

    val bind:
        expectedCallerRepository: string ->
        expectedInventoryId: string ->
        call: ReusableWorkflowCall ->
        facts: PermissionBindingFacts ->
        Result<BoundPermissionCall, string>
