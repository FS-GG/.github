namespace FS.GG.Coord.Tests

open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord

module EfficiencyEvidenceTests =
    let private encode (text: string) =
        use document = JsonDocument.Parse text
        EfficiencyEvidence.encode document.RootElement

    [<Fact>]
    let ``private codec matches independently emitted Python Unicode control and scalar key bytes`` () =
        let raw = """{"é":"😀","":127,"😀":"","quote":"\"\\\n","null":null,"values":[true,0,-1]}"""
        let expected = [|123uy;34uy;110uy;117uy;108uy;108uy;34uy;58uy;110uy;117uy;108uy;108uy;44uy;34uy;113uy;117uy;111uy;116uy;101uy;34uy;58uy;34uy;92uy;34uy;92uy;92uy;92uy;110uy;34uy;44uy;34uy;118uy;97uy;108uy;117uy;101uy;115uy;34uy;58uy;91uy;116uy;114uy;117uy;101uy;44uy;48uy;44uy;45uy;49uy;93uy;44uy;34uy;92uy;117uy;48uy;48uy;101uy;57uy;34uy;58uy;34uy;92uy;117uy;100uy;56uy;51uy;100uy;92uy;117uy;100uy;101uy;48uy;48uy;34uy;44uy;34uy;92uy;117uy;101uy;48uy;48uy;48uy;34uy;58uy;49uy;50uy;55uy;44uy;34uy;92uy;117uy;100uy;56uy;51uy;100uy;92uy;117uy;100uy;101uy;48uy;48uy;34uy;58uy;34uy;92uy;117uy;48uy;48uy;55uy;102uy;34uy;125uy|]
        match encode raw with
        | Ok actual -> Assert.Equal<byte>(expected, actual)
        | Error reason -> failwith reason

    [<Fact>]
    let ``duplicate source properties do not acquire a new digest`` () =
        Assert.True(match encode """{"id":1,"id":2}""" with Error _ -> true | _ -> false)

    [<Fact>]
    let ``noninteger counters require explicit unavailable treatment`` () =
        Assert.True(match encode """{"amount":0.5}""" with Error _ -> true | _ -> false)

    [<Fact>]
    let ``retained packet rejects byte reformatting`` () =
        let raw = Encoding.UTF8.GetBytes """{"coverage":{"classification":"unknown","dependency":"unknown","lineage":"unknown","population":"unknown","usage":"unknown"},"omissions":[],"records":[],"schema":"fsgg.telemetry.efficiency-evidence-packet/1","subject":{"itemId":"A","outcomeEpoch":null,"outcomeId":"out","scope":"provisional-delivery"}}"""
        Assert.True(match EfficiencyEvidence.validate raw with Ok _ -> true | _ -> false)
        let changed = Encoding.UTF8.GetBytes """{ "coverage":{"classification":"unknown","dependency":"unknown","lineage":"unknown","population":"unknown","usage":"unknown"},"omissions":[],"records":[],"schema":"fsgg.telemetry.efficiency-evidence-packet/1","subject":{"itemId":"A","outcomeEpoch":null,"outcomeId":"out","scope":"provisional-delivery"}}"""
        Assert.True(match EfficiencyEvidence.validate changed with Error _ -> true | _ -> false)

    [<Fact>]
    let ``private capture has finite aggregate bounds`` () =
        Assert.True(match EfficiencyEvidence.validate (Array.zeroCreate<byte> (4 * 1024 * 1024 + 1)) with Error _ -> true | _ -> false)

    [<Theory>]
    [<InlineData("0.5")>]
    [<InlineData("9223372036854775808")>]
    let ``semantic reference fractional or oversized revision is a Result refusal`` (revision: string) =
        let digest = String.replicate 64 "a"
        let raw = """{"coverage":{"classification":"unknown","dependency":"unknown","lineage":"unknown","population":"unknown","usage":"unknown"},"omissions":[],"records":[{"analysisGenerated":false,"canonicalRef":{"contentDigest":"sha256:""" + digest + """","id":"u","kind":"usage","revision":0},"itemId":"A","payload":{},"priority":"other","ref":{"id":"u","kind":"usage","revision":""" + revision + """}}],"schema":"fsgg.telemetry.efficiency-evidence-packet/1","subject":{"itemId":"A","outcomeEpoch":null,"outcomeId":"out","scope":"provisional-delivery"}}"""
        Assert.True(match EfficiencyEvidence.validate (Encoding.UTF8.GetBytes raw) with Error _ -> true | _ -> false)

    let private validEmptyPacket =
        """{"coverage":{"classification":"unknown","dependency":"unknown","lineage":"unknown","population":"unknown","usage":"unknown"},"omissions":[],"records":[],"schema":"fsgg.telemetry.efficiency-evidence-packet/1","subject":{"itemId":"A","outcomeEpoch":null,"outcomeId":"out","scope":"provisional-delivery"}}"""

    [<Theory>]
    [<InlineData("coverage", "{}")>]
    [<InlineData("coverage", "{\"population\":\"complete\",\"usage\":\"complete\",\"classification\":\"complete\",\"lineage\":\"complete\",\"dependency\":\"invented\"}")>]
    [<InlineData("omissions", "[1]")>]
    [<InlineData("subject", "{}")>]
    let ``closed packet subject coverage and omission shapes refuse malformed values`` (field: string) (replacement: string) =
        let node = System.Text.Json.Nodes.JsonNode.Parse validEmptyPacket
        node[field] <- System.Text.Json.Nodes.JsonNode.Parse replacement
        Assert.True(match EfficiencyEvidence.validate (Encoding.UTF8.GetBytes(node.ToJsonString())) with Error _ -> true | _ -> false)

    [<Fact>]
    let ``duplicate packet reference population cannot masquerade as two sources`` () =
        let digest = String.replicate 64 "a"
        let row = System.Text.Json.Nodes.JsonNode.Parse("""{"analysisGenerated":false,"canonicalRef":{"contentDigest":"sha256:""" + digest + """","id":"u","kind":"usage","revision":0},"itemId":"A","payload":{},"priority":"other","ref":{"id":"u","kind":"usage","revision":0}}""")
        let node = System.Text.Json.Nodes.JsonNode.Parse validEmptyPacket
        node["records"].AsArray().Add row
        node["records"].AsArray().Add(row.DeepClone())
        use document = JsonDocument.Parse(node.ToJsonString())
        let bytes = EfficiencyEvidence.encode document.RootElement |> Result.defaultWith failwith
        Assert.True(match EfficiencyEvidence.validate bytes with Error _ -> true | _ -> false)
