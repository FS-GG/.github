module FS.GG.Coord.GitHub.Tests.GraphQlBoundaryTests

open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors

type private Item = { Id: string }

let private item (node: JsonElement) =
    match node.TryGetProperty "id" with
    | true, value when value.ValueKind = JsonValueKind.String -> Ok { Id = value.GetString() }
    | _ -> Error(Malformed("fixture", "missing id"))

let private page subject body =
    GraphQl.decode subject body (fun data ->
        GraphQl.page subject "fixture connection" (fun value -> value.Id) item (data.GetProperty "connection"))

[<Fact>]
let ``mixed data and errors is never a successful typed value`` () =
    let body = """{"data":{"answer":42},"errors":[{"message":"field failed"}]}"""
    match GraphQl.decode "mixed" body (fun data -> Ok(data.GetProperty("answer").GetInt32())) with
    | Error(GraphQlErrors [ "field failed" ]) -> ()
    | other -> failwith $"expected generic GraphQL refusal, got %A{other}"

[<Fact>]
let ``rate limit carries retry and rate metadata`` () =
    let body = """{"data":{"answer":42},"errors":[{"message":"API rate limit exceeded"}]}"""
    match GraphQl.decode "limited" body (fun _ -> Ok 42) with
    | Error error ->
        let metadata = GraphQl.classify error
        match metadata.Retry, metadata.RateLimit with
        | GraphQl.Retryable Primary, Some(GraphQlBudget, None) -> ()
        | other -> failwith $"expected primary GraphQL retry metadata, got %A{other}"
    | Ok value -> failwith $"partial response escaped as %d{value}"

[<Fact>]
let ``repeated cursor refuses instead of looping or succeeding short`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"same"}}}}"""
          """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"b"}],"pageInfo":{"hasNextPage":true,"endCursor":"same"}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "repeat" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "repeat" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("repeated cursor", detail)
    | other -> failwith $"expected repeated-cursor refusal, got %A{other}"

[<Fact>]
let ``duplicate identity across pages exposes page-boundary mutation`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
          """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "mutation" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "mutation" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("mutated", detail)
    | other -> failwith $"expected mutation refusal, got %A{other}"

[<Fact>]
let ``changing total count across pages is a typed incomplete read`` () =
    let bodies =
        [ """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"}],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
          """{"data":{"connection":{"totalCount":3,"nodes":[{"id":"b"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}""" ]
    let mutable index = 0
    let fetch _ = let result = page "count" bodies.[index] in index <- index + 1; result
    match GraphQl.drain "count" "fixture connection" { MaxPages = 5; MaxItems = 10 } fetch with
    | Error(Malformed(_, detail)) -> Assert.Contains("totalCount changed", detail)
    | other -> failwith $"expected count-mutation refusal, got %A{other}"

[<Fact>]
let ``empty continuing page and explicit item limit both fail closed`` () =
    let empty = """{"data":{"connection":{"totalCount":1,"nodes":[],"pageInfo":{"hasNextPage":true,"endCursor":"one"}}}}"""
    match page "empty" empty with
    | Error(Malformed(_, detail)) -> Assert.Contains("empty page", detail)
    | other -> failwith $"expected empty-page refusal, got %A{other}"

    let full = """{"data":{"connection":{"totalCount":2,"nodes":[{"id":"a"},{"id":"b"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}"""
    match GraphQl.drain "limit" "fixture connection" { MaxPages = 1; MaxItems = 1 } (fun _ -> page "limit" full) with
    | Error(Malformed(_, detail)) -> Assert.Contains("item limit", detail)
    | other -> failwith $"expected explicit-limit refusal, got %A{other}"
