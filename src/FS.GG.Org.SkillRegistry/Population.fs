namespace FS.GG.Org.SkillRegistry

open System
open System.Text.RegularExpressions

/// One row in the organization catalog, after syntax parsing by a future adapter.
type RegistryRow = { Id: string; Owner: string; Source: string; Sha256: string }

/// One declared skill from a producer's already parsed manifest.
type ManifestEntry = { Id: string; SuppliedBy: string option; Sha256: string }

/// A manifest is present and parsed, unreadable, or absent at the producer's known roots.
type ManifestState = Parsed of ManifestEntry list | Unreadable of string | Absent

/// One producer checkout. A rostered repository may legitimately have no skill manifest.
type Checkout = { Repo: string; Manifest: ManifestState }

/// Inputs whose roster and reachable population were independently enumerated by the caller.
/// A missing or unreadable roster must be carried as Error, never converted to an empty set.
type PopulationInput = { Rostered: Result<string list, string>; Checkouts: Checkout list; Rows: RegistryRow list }

/// A deterministic refusal; no input is silently dropped from the population check.
type PopulationFinding = { Code: string; Subject: string; Detail: string }

/// Pure population, identity and source-path closure. No filesystem or registry write occurs here.
module Population =
    let private finding code subject detail = { Code = code; Subject = subject; Detail = detail }
    let private digest = Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)
    let private repoName = Regex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)

    let private validRepoName (name: string) =
        not (String.IsNullOrWhiteSpace name)
        && name <> "."
        && name <> ".."
        && repoName.IsMatch name

    let private ownerOf repo =
        if repo = ".github" then ".github"
        else repo.ToLowerInvariant().Replace('.', '-')

    let private safePath (path: string) =
        not (String.IsNullOrWhiteSpace path)
        && not (path.StartsWith("/", StringComparison.Ordinal))
        && not (path.Contains('\\'))
        && not (path.Contains(':'))
        && (path.Split('/') |> Array.forall (fun part -> part <> "" && part <> "." && part <> ".."))

    let private safeSuppliedBy (path: string) = safePath (path.TrimEnd('/'))

    let private sourceRepo (source: string) =
        if not (safePath source) then None
        else
            match source.Split('/') with
            | [| _ |] -> None
            | parts -> Some parts.[0]

    let private duplicates code (items: seq<string>) =
        items
        |> Seq.countBy id
        |> Seq.choose (fun (name, count) -> if count > 1 then Some (finding code name (sprintf "declared %d times" count)) else None)
        |> Seq.toList

    let private caseCollisions code (items: seq<string>) =
        items
        |> Seq.filter validRepoName
        |> Seq.groupBy (fun name -> name.ToUpperInvariant())
        |> Seq.choose (fun (_, names) ->
            let spellings = names |> Seq.distinct |> Seq.sort |> Seq.toList
            if spellings.Length > 1 then Some (finding code (String.concat " / " spellings) "repository names differ only by case")
            else None)
        |> Seq.toList

    let private ownerCollisions code (items: seq<string>) =
        items
        |> Seq.filter validRepoName
        |> Seq.groupBy ownerOf
        |> Seq.choose (fun (owner, names) ->
            let repos = names |> Seq.distinct |> Seq.sort |> Seq.toList
            if repos.Length > 1 then Some (finding code owner ("repository owner identity is shared by " + String.concat ", " repos))
            else None)
        |> Seq.toList

    /// Inspect one enumerated population. Findings are sorted for stable test and command output.
    let inspect (input: PopulationInput) : PopulationFinding list =
        let rostered =
            match input.Rostered with
            | Ok names -> names
            | Error _ -> []
        let checkoutByRepo = input.Checkouts |> Seq.map (fun checkout -> checkout.Repo, checkout) |> Map.ofSeq
        let rowById = input.Rows |> Seq.map (fun row -> row.Id, row) |> Map.ofSeq
        let named = input.Rows |> List.choose (fun row -> sourceRepo row.Source) |> Set.ofList
        let roots = input.Checkouts |> List.map (fun checkout -> checkout.Repo) |> Set.ofList
        let mutable findings = []
        let add value = findings <- value :: findings

        match input.Rostered with
        | Error reason -> add (finding "roster-unreadable" "registry/repos.yml" reason)
        | Ok [] -> add (finding "roster-empty" "registry/repos.yml" "readable roster names no FS-GG repositories")
        | Ok _ -> ()

        for item in duplicates "roster-duplicate" rostered do add item
        for item in duplicates "checkout-duplicate" (input.Checkouts |> Seq.map (fun checkout -> checkout.Repo)) do add item
        for item in duplicates "registry-duplicate" (input.Rows |> Seq.map (fun row -> row.Id)) do add item
        for item in caseCollisions "roster-case-collision" rostered do add item
        for item in caseCollisions "checkout-case-collision" (input.Checkouts |> Seq.map (fun checkout -> checkout.Repo)) do add item
        for item in ownerCollisions "roster-owner-collision" rostered do add item
        for item in ownerCollisions "checkout-owner-collision" (input.Checkouts |> Seq.map (fun checkout -> checkout.Repo)) do add item

        for repo in rostered do
            if not (validRepoName repo) then add (finding "roster-name" repo "roster must contain an FS-GG checkout name")

        for checkout in input.Checkouts do
            if not (validRepoName checkout.Repo) then add (finding "checkout-name" checkout.Repo "checkout must have a contained repository name")

        for repo in rostered |> Set.ofList do
            if not (roots.Contains repo) then add (finding "roster-unreachable" repo "rostered repository has no checkout")

        for row in input.Rows do
            if String.IsNullOrWhiteSpace row.Id then add (finding "row-id" "<empty>" "registry skill id is empty")
            if isNull row.Sha256 || not (digest.IsMatch row.Sha256) then add (finding "row-digest" row.Id "digest must be 64 lowercase hex characters")
            match sourceRepo row.Source with
            | None -> add (finding "source-path" row.Id "source must be a contained repo-relative file path")
            | Some _ when not (row.Source.EndsWith("/SKILL.md", StringComparison.Ordinal)) ->
                add (finding "source-path" row.Id "source must name a SKILL.md file")
            | Some repo when row.Owner <> ownerOf repo ->
                add (finding "source-owner" row.Id (sprintf "owner %s disagrees with source repository %s" row.Owner repo))
            | _ -> ()

        for repo in named do
            match checkoutByRepo |> Map.tryFind repo with
            | None -> add (finding "manifest-unreachable" repo "registry names a producer without a checkout")
            | Some { Manifest = Absent } -> add (finding "manifest-missing" repo "named producer has no skill manifest")
            | Some { Manifest = Unreadable reason } -> add (finding "manifest-unreadable" repo reason)
            | Some _ -> ()

        let declarations =
            input.Checkouts
            |> List.collect (fun checkout ->
                match checkout.Manifest with
                | Parsed entries ->
                    for item in duplicates "manifest-duplicate" (entries |> Seq.map (fun entry -> entry.Id)) do
                        add { item with Subject = checkout.Repo + "/" + item.Subject }
                    entries |> List.map (fun entry -> checkout.Repo, entry)
                | Unreadable reason when not (named.Contains checkout.Repo) ->
                    add (finding "manifest-unreadable" checkout.Repo reason)
                    []
                | _ -> [])

        for (repo, entry) in declarations do
            if String.IsNullOrWhiteSpace entry.Id then add (finding "manifest-id" repo "manifest skill id is empty")
            if isNull entry.Sha256 || not (digest.IsMatch entry.Sha256) then add (finding "manifest-digest" (repo + "/" + entry.Id) "digest must be 64 lowercase hex characters")
            match entry.SuppliedBy with
            | Some path when not (safeSuppliedBy path) -> add (finding "supplied-by-path" (repo + "/" + entry.Id) "supplied-by must be a contained relative directory")
            | None when repo <> "FS.GG.SDD" -> add (finding "supplied-by-missing" (repo + "/" + entry.Id) "producer manifest cannot bind this skill to its registry source")
            | _ -> ()

        for (skillId, declarers) in declarations |> Seq.groupBy (fun (_, entry) -> entry.Id) do
            match rowById |> Map.tryFind skillId with
            | None ->
                if Seq.length declarers > 1 then
                    add (finding "missing-ambiguous" skillId "multiple producers declare an absent row; owner cannot be inferred")
                else add (finding "missing-row" skillId "producer declares a skill absent from the registry")
            | Some row ->
                let owned = declarers |> Seq.filter (fun (repo, _) -> ownerOf repo = row.Owner) |> Seq.toList
                if owned.IsEmpty then add (finding "owner-unmatched" skillId "no declaring producer matches the registry owner")
                else
                    for (repo, entry) in owned do
                        if row.Sha256 <> entry.Sha256 then add (finding "manifest-digest-mismatch" skillId (repo + " declares a different digest"))
                        match entry.SuppliedBy with
                        | Some suppliedBy when safeSuppliedBy suppliedBy ->
                            let expected = repo + "/" + suppliedBy.TrimEnd('/') + "/SKILL.md"
                            if row.Source <> expected then add (finding "source-identity" skillId ("registry source differs from " + expected))
                        | _ -> ()

        // A named producer can publish a readable but empty manifest. Row-to-manifest closure
        // must therefore run in this direction too, or a withdrawn declaration looks clean.
        for row in input.Rows do
            match sourceRepo row.Source with
            | Some repo ->
                match checkoutByRepo |> Map.tryFind repo with
                | Some { Manifest = Parsed entries } when entries |> List.exists (fun entry -> entry.Id = row.Id) |> not ->
                    add (finding "row-undeclared" row.Id (repo + " manifest does not declare this registry row"))
                | _ -> ()
            | None -> ()

        findings |> List.distinct |> List.sortBy (fun item -> item.Code, item.Subject, item.Detail)
