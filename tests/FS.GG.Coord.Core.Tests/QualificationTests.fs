namespace FS.GG.Coord.Tests

open Xunit
open FS.GG.Coord
open FS.GG.Coord.Qualification

module QualificationTests =
    let private sha c = System.String(c, 64)
    let private revision = System.String('1', 40)
    let private tool = { Id = "dotnet"; Version = "10.0.0"; Sha256 = sha 'a' }
    let private executor = { Id = "executor-primary"; Role = "implementer"; ImplementationSha256 = sha 'b' }

    let private operation id kind =
        { Id = id
          Kind = kind
          SubjectRevision = revision
          Tool = tool
          Executor = executor
          CommandSha256 = sha 'c'
          ArtifactSha256 = [ sha 'd' ]
          ResultSha256 = sha 'e'
          ReplayResultSha256 = if kind = FixedPoint then Some(sha 'e') else None
          ExitCode = if kind = Mutation then 3 else 0
          Refusal = if kind = Mutation then Some "REFUSED wrong subject" else None }

    let private acceptedInput () =
        let operations =
            [ operation "analyze" Analyze
              operation "verify" Verify
              operation "ship" Ship
              operation "hosted" Hosted
              operation "fixed" FixedPoint
              operation "mutation-wrong-subject" Mutation ]
        let hostedChecks =
            [ { Scope = "run"; Id = "100"; SubjectRevision = revision; State = "completed"; Conclusion = "success" }
              { Scope = "job"; Id = "200"; SubjectRevision = revision; State = "completed"; Conclusion = "success" }
              { Scope = "check"; Id = "300"; SubjectRevision = revision; State = "completed"; Conclusion = "success" } ]
        { Schema = InputSchema
          Subject = "FS-GG/.github#3209"
          SubjectRevision = revision
          CheckoutClean = true
          ToolManifest = [ tool ]
          Executor = executor
          Operations = operations
          Claims =
            [ { Id = "full-qualification"
                SubjectRevision = revision
                RequiredKinds = [ Analyze; Verify; Ship; Hosted; FixedPoint ]
                EvidenceIds = [ "analyze"; "verify"; "ship"; "hosted"; "fixed" ] } ]
          Mutations =
            [ { Id = "wrong-subject"
                OperationId = "mutation-wrong-subject"
                ExpectedRefusal = "REFUSED wrong subject"
                ObservedRefusal = "REFUSED wrong subject"
                ProductionImplementationSha256 = executor.ImplementationSha256
                FixtureImplementationSha256 = sha 'f'
                FixtureExecutorId = "fixture-executor"
                FixtureExecutorRole = "mutation-fixture" } ]
          HostedObservations = [ { Complete = true; Checks = hostedChecks }; { Complete = true; Checks = hostedChecks } ]
          Obligations =
            { HeadSha = revision
              Declarations = [ NoObligations ]
              Readback =
                Some
                    { CommentId = 1L
                      Url = "https://github.com/FS-GG/.github/pull/1#issuecomment-1"
                      Author = "github-actions[bot]" } }
          SemanticReview = { SubjectRevision = revision; Accepted = true; Evidence = "https://github.com/FS-GG/.github/pull/1#issuecomment-1" } }

    let private findings input =
        match Qualification.validate input with
        | Ok _ -> failwith "expected qualification refusal"
        | Error values -> values

    let private contains (predicate: Finding -> bool) (values: Finding list) =
        Assert.Contains(values, System.Predicate<Finding> predicate)

    [<Fact>]
    let ``#3209 exact clean qualification accepts and replays canonical bytes`` () =
        let input = acceptedInput ()
        let first = Qualification.validate input |> Result.defaultWith (fun values -> failwithf "%A" values)
        let second = Qualification.validate input |> Result.defaultWith (fun values -> failwithf "%A" values)
        Assert.Equal(first, second)
        Assert.Equal(Qualification.canonicalResult first, Qualification.canonicalResult second)
        Assert.Equal(64, first.Digest.Length)
        Assert.Equal(64, first.EvidenceDigest.Length)
        Assert.Equal(3, first.HostedCheckCount)
        Assert.Equal(0, first.ObligationCount)
        let changed =
            { input with Operations = { input.Operations.Head with CommandSha256 = sha '9' } :: input.Operations.Tail }
            |> Qualification.validate
            |> Result.defaultWith (fun values -> failwithf "%A" values)
        Assert.False(first.EvidenceDigest = changed.EvidenceDigest)

    [<Fact>]
    let ``#3209 dirty checkout and stale subject both fail closed`` () =
        let stale = { operation "analyze" Analyze with SubjectRevision = System.String('2', 40) }
        let input = acceptedInput ()
        let values = findings { input with CheckoutClean = false; Operations = stale :: input.Operations.Tail }
        contains (function DirtyCheckout -> true | _ -> false) values
        contains (function StaleSubject("analyze", _) -> true | _ -> false) values

    [<Fact>]
    let ``#3209 undeclared and drifted tool identities are distinct refusals`` () =
        let input = acceptedInput ()
        let undeclared = { input.Operations.Head with Tool = { tool with Id = "fake" } }
        contains (function UndeclaredTool("analyze", "fake") -> true | _ -> false) (findings { input with Operations = undeclared :: input.Operations.Tail })
        let drifted = { input.Operations.Head with Tool = { tool with Version = "9.9.9" } }
        contains (function ToolIdentityMismatch("analyze", "dotnet") -> true | _ -> false) (findings { input with Operations = drifted :: input.Operations.Tail })

    [<Fact>]
    let ``#3209 wrong executor and malformed provenance digest are refused`` () =
        let input = acceptedInput ()
        let wrong = { input.Operations.Head with Executor = { executor with Id = "other" }; CommandSha256 = "not-a-digest" }
        let values = findings { input with Operations = wrong :: input.Operations.Tail }
        contains (function WrongExecutor("analyze", "other") -> true | _ -> false) values
        contains (function InvalidDigest(field, _) when field.Contains "commandSha256" -> true | _ -> false) values

    [<Fact>]
    let ``#3209 operation order and required lifecycle kinds are closed`` () =
        let input = acceptedInput ()
        let withoutShip = input.Operations |> List.filter (fun operation -> operation.Kind <> Ship)
        let values = findings { input with Operations = List.rev withoutShip }
        contains (function MissingOperationKind Ship -> true | _ -> false) values
        contains (function OperationOrderMismatch _ -> true | _ -> false) values

    [<Fact>]
    let ``#3209 fixed-point replay binds exact result digest`` () =
        let input = acceptedInput ()
        let operations = input.Operations |> List.map (fun operation -> if operation.Kind = FixedPoint then { operation with ReplayResultSha256 = Some(sha '9') } else operation)
        contains (function FixedPointMismatch "fixed" -> true | _ -> false) (findings { input with Operations = operations })

    [<Fact>]
    let ``#3209 claims require existing subject-matched adequate evidence`` () =
        let input = acceptedInput ()
        let claim = { input.Claims.Head with RequiredKinds = [ Ship ]; EvidenceIds = [ "missing"; "verify" ] }
        let values = findings { input with Claims = [ claim ] }
        contains (function ClaimEvidenceMissing("full-qualification", "missing") -> true | _ -> false) values
        contains (function InadequateClaimEvidence("full-qualification", Ship) -> true | _ -> false) values

    [<Fact>]
    let ``#3209 mutation fixture must be independent and observe exact refusal`` () =
        let input = acceptedInput ()
        let mutation =
            { input.Mutations.Head with
                ObservedRefusal = "REFUSED something else"
                FixtureImplementationSha256 = executor.ImplementationSha256
                FixtureExecutorId = executor.Id
                FixtureExecutorRole = executor.Role }
        let values = findings { input with Mutations = [ mutation ] }
        contains (function MutationRefusalMismatch "wrong-subject" -> true | _ -> false) values
        contains (function MutationFixtureNotIndependent "wrong-subject" -> true | _ -> false) values

    [<Fact>]
    let ``#3209 hosted observations reject pending foreign and growing sets`` () =
        let input = acceptedInput ()
        let first = input.HostedObservations.Head
        let pending = { first.Checks.Head with SubjectRevision = System.String('2', 40); State = "in_progress"; Conclusion = "" }
        let second = { Complete = false; Checks = pending :: first.Checks.Tail @ [ { first.Checks.Head with Id = "new" } ] }
        let values = findings { input with HostedObservations = [ first; second ] }
        contains (function HostedObservationIncomplete 2 -> true | _ -> false) values
        contains (function HostedForeignSubject("100", _) -> true | _ -> false) values
        contains (function HostedCheckPending "100" -> true | _ -> false) values
        contains (function HostedSetNotConverged -> true | _ -> false) values

    [<Fact>]
    let ``#3209 obligation declaration is exact current-head singleton`` () =
        let input = acceptedInput ()
        let stale = { input.Obligations with HeadSha = System.String('2', 40); Declarations = [ NoObligations; Obligations [ "publish"; "publish" ] ] }
        let values = findings { input with Obligations = stale }
        contains (function ObligationHeadMismatch _ -> true | _ -> false) values
        contains (function ObligationDeclarationDuplicate 2 -> true | _ -> false) values

    [<Fact>]
    let ``#3209 duplicate obligation ids and absent declaration are refused`` () =
        let input = acceptedInput ()
        contains (function ObligationDeclarationMissing -> true | _ -> false) (findings { input with Obligations = { input.Obligations with Declarations = [] } })
        contains (function ObligationIdDuplicate "publish" -> true | _ -> false) (findings { input with Obligations = { input.Obligations with Declarations = [ Obligations [ "publish"; "publish" ] ] } })

    [<Fact>]
    let ``#3209 obligation acceptance requires authoritative readback identity`` () =
        let input = acceptedInput ()
        contains (function ObligationReadbackMissing -> true | _ -> false) (findings { input with Obligations = { input.Obligations with Readback = None } })
        let invalid = { CommentId = 0L; Url = "file:///tmp/asserted"; Author = "" }
        contains (function ObligationReadbackInvalid -> true | _ -> false) (findings { input with Obligations = { input.Obligations with Readback = Some invalid } })

    [<Fact>]
    let ``#3209 semantic review is mandatory and exact-subject`` () =
        let input = acceptedInput ()
        let values = findings { input with SemanticReview = { SubjectRevision = System.String('2', 40); Accepted = false; Evidence = "" } }
        contains (function SemanticReviewMissing -> true | _ -> false) values
        contains (function SemanticReviewStale _ -> true | _ -> false) values

    [<Fact>]
    let ``#3209 duplicate manifest and evidence identities refuse before acceptance`` () =
        let input = acceptedInput ()
        let values = findings { input with ToolManifest = [ tool; tool ]; Operations = input.Operations @ [ input.Operations.Head ] }
        contains (function DuplicateIdentity("tool", "dotnet") -> true | _ -> false) values
        contains (function DuplicateIdentity("operation", "analyze") -> true | _ -> false) values
