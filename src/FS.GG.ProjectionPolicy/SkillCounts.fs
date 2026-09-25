namespace FS.GG.ProjectionPolicy

open System

type SkillRow = { Id: string; Scope: string; Owner: string }

module SkillCounts =
    let private scopes = set [ "process"; "product"; "driver"; "operator" ]
    let private safeToken (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value |> Seq.forall (fun letter ->
            (letter >= 'a' && letter <= 'z')
            || (letter >= '0' && letter <= '9')
            || letter = '-' || letter = '.' || letter = '_')

    /// Pure dynamic body for generate-projections' skill-registry-counts region.
    /// Parsing, catalog completeness, and publication remain with their owners.
    let render (rows: SkillRow list) : Result<string, string> =
        if List.isEmpty rows then Error "skill catalog is empty"
        elif rows |> List.exists (fun row -> not (safeToken row.Id) || not (safeToken row.Owner) || not (scopes.Contains row.Scope)) then
            Error "skill row has missing identity, owner, or unsupported scope"
        elif rows |> List.map _.Id |> Set.ofList |> Set.count <> rows.Length then
            Error "skill catalog repeats an id"
        else
            let count scope = rows |> List.filter (fun row -> row.Scope = scope) |> List.length
            let productByOwner =
                rows
                |> List.filter (fun row -> row.Scope = "product")
                |> List.groupBy _.Owner
                |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
                |> List.map (fun (owner, group) -> sprintf "%d `%s`" group.Length owner)
                |> String.concat " + "
            let summary =
                sprintf "**%d rows** = **%d process** + **%d product**" rows.Length (count "process") (count "product")
                + (if count "driver" > 0 then sprintf " + **%d driver**" (count "driver") else "")
                + (if count "operator" > 0 then sprintf " + **%d operator**" (count "operator") else "")
                + sprintf " (%s)." productByOwner
            let table =
                rows
                |> List.groupBy (fun row -> row.Scope, row.Owner)
                |> List.sortWith (fun ((leftScope, leftOwner), _) ((rightScope, rightOwner), _) ->
                    let scopeOrder = StringComparer.Ordinal.Compare(leftScope, rightScope)
                    if scopeOrder <> 0 then scopeOrder else StringComparer.Ordinal.Compare(leftOwner, rightOwner))
                |> List.map (fun ((scope, owner), group) -> sprintf "| %s | `%s` | %d |" scope owner group.Length)
                |> String.concat "\n"
            Ok(sprintf "%s\n\n| scope | owner | rows |\n|---|---|---|\n%s\n| **total** | | **%d** |" summary table rows.Length)
