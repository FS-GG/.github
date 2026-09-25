namespace FS.GG.Org.PermissionPolicy

open System
open System.Text.RegularExpressions

/// A provider-supplied read of the callee named by one reusable-workflow call.
type CalleeOrigin = WorkingTree | ExactRefRead

type CalleeContentFact =
    {
        Repository: string
        WorkflowPath: string
        Ref: string
        Origin: CalleeOrigin
        Text: string
    }

type RosterFact =
    {
        Repository: string
        Path: string
        Repositories: string list
    }

/// The pinned inventory is a separate fact; its provider read remains external.
type AppGrantFact =
    {
        Repository: string
        InventoryId: string
        Grants: (string * PermissionLevel) list
    }

type PermissionBindingFacts =
    {
        CallerRepository: string
        Roster: RosterFact option
        Callee: CalleeContentFact option
        AppGrants: AppGrantFact option
    }

type BoundPermissionCall =
    {
        CallerRepository: string
        Call: ReusableWorkflowCall
        CalleeText: string
        AppGrants: AppGrantFact
    }

/// Pure identity checks for provider facts. This module does not fetch, authenticate or compare grants.
[<RequireQualifiedAccess>]
module PermissionEvidenceBinding =
    let private authority = "FS-GG/.github"
    let private rosterPath = "registry/repos.yml"
    let private scopeName = Regex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)

    let validateAppGrant expectedInventoryId (grants: AppGrantFact) =
        if String.IsNullOrWhiteSpace expectedInventoryId then Error "app-grants-identity-missing"
        elif grants.Repository <> authority then Error "app-grants-source-mismatch"
        elif grants.InventoryId <> expectedInventoryId then Error "app-grants-identity-mismatch"
        elif List.isEmpty grants.Grants
             || (grants.Grants |> List.exists (fun (scope, _) ->
                 String.IsNullOrWhiteSpace scope || not (scopeName.IsMatch scope)))
             || (grants.Grants |> List.map fst |> Set.ofList |> Set.count) <> grants.Grants.Length then
            Error "app-grants-invalid"
        else Ok grants

    let bind (expectedCallerRepository: string) (expectedInventoryId: string)
             (call: ReusableWorkflowCall)
             (facts: PermissionBindingFacts) : Result<BoundPermissionCall, string> =
        if String.IsNullOrWhiteSpace expectedCallerRepository then
            Error "caller-repository-missing"
        elif String.IsNullOrWhiteSpace expectedInventoryId then
            Error "app-grants-identity-missing"
        elif facts.CallerRepository <> expectedCallerRepository then
            Error "caller-repository-mismatch"
        else
            match facts.Roster with
            | None -> Error "roster-missing"
            | Some roster when roster.Repository <> authority || roster.Path <> rosterPath ->
                Error "roster-source-mismatch"
            | Some roster when List.isEmpty roster.Repositories
                               || (roster.Repositories |> List.exists String.IsNullOrWhiteSpace)
                               || (roster.Repositories |> Set.ofList |> Set.count) <> roster.Repositories.Length ->
                Error "roster-invalid"
            | Some roster when not (List.contains expectedCallerRepository roster.Repositories) ->
                Error "caller-not-rostered"
            | Some _ ->
                match facts.Callee with
                | None -> Error "callee-fact-missing"
                | Some callee when callee.Repository <> authority -> Error "callee-repository-mismatch"
                | Some callee when callee.WorkflowPath <> ".github/workflows/" + call.Callee ->
                    Error "callee-path-mismatch"
                | Some callee when callee.Ref <> call.Ref -> Error "callee-ref-mismatch"
                | Some callee when (call.Ref = "main" && callee.Origin <> WorkingTree)
                                   || (call.Ref <> "main" && callee.Origin <> ExactRefRead) ->
                    Error "callee-origin-mismatch"
                | Some callee when String.IsNullOrWhiteSpace callee.Text -> Error "callee-text-missing"
                | Some callee ->
                    match facts.AppGrants with
                    | None -> Error "app-grants-missing"
                    | Some grants ->
                        validateAppGrant expectedInventoryId grants
                        |> Result.map (fun valid ->
                            {
                                CallerRepository = expectedCallerRepository
                                Call = call
                                CalleeText = callee.Text
                                AppGrants = valid
                            })
