namespace FS.GG.Org.Manifest.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open FS.GG.Org.Manifest
open Xunit

module DriverManifestTests =
    let root = ".claude/skills"
    let hash = String.replicate 64 "a"
    // Independent Python/BCL vector for `[{'path':'SKILL.md','sha256':'a'*64,'executable':false}]`.
    let tree = "041fb3d7a245008ec8ca0603ebbb014cf326ecf7e3ff424495fd99a0abf59484"
    let file = sprintf """{"path":"SKILL.md","sha256":"%s","executable":false}""" hash

    let skill id suppliedBy files =
        sprintf
            """{"id":"%s","scope":"driver","sha256":"%s","tree-sha256":"%s","files":[%s],"supplied-by":"%s","materializes-when":"always"}"""
            id hash tree files suppliedBy

    let document rows = sprintf """{"schemaVersion":2,"skills":[%s]}""" rows
    let valid = document (skill "work-roadmap" ".claude/skills/work-roadmap" file)

    let errors json =
        match DriverManifest.parse root json with
        | Ok _ -> failwith "Expected a refusal."
        | Error messages -> String.concat "; " messages

    [<Fact>]
    let ``accepts a bound schema-v2 driver manifest`` () =
        match DriverManifest.parse root valid with
        | Ok _ -> ()
        | Error messages -> failwithf "Expected valid manifest: %A" messages

    [<Fact>]
    let ``refuses a missing producer declaration`` () =
        let missing = valid.Replace("\"supplied-by\":\".claude/skills/work-roadmap\",", "")
        Assert.Contains("supplied-by", errors missing)

    [<Fact>]
    let ``refuses a producer under the wrong skill root`` () =
        let wrong = valid.Replace(".claude/skills/work-roadmap", ".agents/skills/work-roadmap")
        Assert.Contains("wrong producer root", errors wrong)

    [<Fact>]
    let ``refuses duplicate skill and file entries`` () =
        let row = skill "work-roadmap" ".claude/skills/work-roadmap" file
        Assert.Contains("duplicate id", errors (document (row + "," + row)))
        Assert.Contains("duplicate path", errors (document (skill "work-roadmap" ".claude/skills/work-roadmap" (file + "," + file))))

    [<Fact>]
    let ``refuses duplicate JSON properties at every manifest level`` () =
        let rootDuplicate = valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":0,\"schemaVersion\":2")
        let skillDuplicate = valid.Replace("\"scope\":\"driver\"", "\"scope\":\"wrong\",\"scope\":\"driver\"")
        let fileDuplicate = valid.Replace("\"executable\":false", "\"executable\":true,\"executable\":false")
        for candidate in [rootDuplicate; skillDuplicate; fileDuplicate] do
            Assert.Contains("duplicate property", errors candidate)

    [<Fact>]
    let ``refuses escaping file paths and a mismatched tree digest`` () =
        Assert.Contains("unsafe relative path", errors (valid.Replace("SKILL.md", "../SKILL.md")))
        Assert.Contains("files digest mismatch", errors (valid.Replace(tree, String.replicate 64 "0")))

    [<Fact>]
    let ``refuses a fractional schema version with a diagnostic`` () =
        Assert.Contains("schemaVersion", errors (valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":2.5")))

    [<Fact>]
    let ``canonical bytes are stable across entry order and repeated parsing`` () =
        let first = skill "alpha" ".claude/skills/alpha" file
        let second = skill "beta" ".claude/skills/beta" file
        let parse json =
            match DriverManifest.parse root json with
            | Ok value -> value
            | Error messages -> failwithf "Expected valid manifest: %A" messages
        let bytes = DriverManifest.renderCanonical (parse (document (second + "," + first)))
        let reversed = DriverManifest.renderCanonical (parse (document (first + "," + second)))
        let repeated = bytes |> Encoding.UTF8.GetString |> parse |> DriverManifest.renderCanonical

        Assert.Equal<byte>(bytes, reversed)
        Assert.Equal<byte>(bytes, repeated)
        Assert.Equal(10uy, bytes[bytes.Length - 1])
        // Python's independent json.dumps(indent=2) rendering of the sorted vector.
        let goldenDigest = "f5cd785adc7fe3f84acb84775145605a7eaa3968d588c4b657ae2214ed9fd0fb"
        let renderedDigest = SHA256.HashData(bytes) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
        Assert.Equal(goldenDigest, renderedDigest)

    [<Fact>]
    let ``current driver manifest is accepted without replacing its producer`` () =
        let rec findRoot directory =
            let candidate = Path.Combine(directory, "registry", "driver-skill-manifest.json")
            if File.Exists candidate then candidate
            else
                let parent = Directory.GetParent directory
                if isNull parent then failwith "Cannot locate the checked-out driver manifest."
                else findRoot parent.FullName
        let path = findRoot AppContext.BaseDirectory
        match DriverManifest.parse root (File.ReadAllText path) with
        | Ok _ -> ()
        | Error messages -> failwithf "Current producer manifest was rejected: %A" messages

    [<Fact>]
    let ``Unicode file names use producer ensure-ascii-false tree bytes`` () =
        let other = String.replicate 64 "b"
        let unicodeFiles = file + "," + sprintf """{"path":"référence.md","sha256":"%s","executable":false}""" other
        let producerTree = "def06be7205d454da0d4cf5fa3e0f7cdbd610ce9e831fd2c28d9594b5a80ee87"
        let escapedTree = "4610075543b787439d3c1fb706e196baa7b41c7ee5301483434da7e4f88839bb"
        let producer = (document (skill "work-roadmap" ".claude/skills/work-roadmap" unicodeFiles)).Replace(tree, producerTree)
        let falseGreen = producer.Replace(producerTree, escapedTree)
        let parsed =
            match DriverManifest.parse root producer with
            | Ok value -> value
            | Error messages -> failwithf "Python producer's Unicode digest must be accepted: %A" messages
        Assert.Contains("files digest mismatch", errors falseGreen)
        let canonical = DriverManifest.renderCanonical parsed
        let actual = SHA256.HashData(canonical) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
        Assert.Equal("ba6c863c004d126b97c4bc48096f5ab7216690cd3a5dd8b0068d8ca0067f8c09", actual)

    [<Fact>]
    let ``producer retains non-BMP and Unicode separator bytes`` () =
        let other = String.replicate 64 "b"
        let separator = "\u2028"
        let unicodeFiles = file + "," + sprintf """{"path":"r%s😃<&.md","sha256":"%s","executable":false}""" separator other
        let producerTree = "20cf26356ef06aef1dd556fd56988bca4eb4a011d03a009c6d482880e47d81c1"
        let producer = (document (skill "work-roadmap" ".claude/skills/work-roadmap" unicodeFiles)).Replace(tree, producerTree)
        match DriverManifest.parse root producer with
        | Ok _ -> ()
        | Error messages -> failwithf "Python producer's Unicode digest must be accepted: %A" messages

    [<Fact>]
    let ``file ordering follows Python Unicode scalar order`` () =
        let privateUse = "\uE000.md"
        let emoji = "😀.md"
        let b = String.replicate 64 "b"
        let c = String.replicate 64 "c"
        let extra path digest = sprintf """{"path":"%s","sha256":"%s","executable":false}""" path digest
        let files = String.concat "," [ file; extra emoji c; extra privateUse b ]
        let producerTree = "cbe23277940ffbb9d2a69ac7dfc943b537de309c31001c264bf985e478d05673"
        let producer = (document (skill "work-roadmap" ".claude/skills/work-roadmap" files)).Replace(tree, producerTree)
        let parsed =
            match DriverManifest.parse root producer with
            | Ok value -> value
            | Error messages -> failwithf "Python producer ordering must be accepted: %A" messages
        let actual = DriverManifest.renderCanonical parsed |> SHA256.HashData |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
        Assert.Equal("2fe213766dd2577af069c74d03c054cf48f61b9e57390bf4216a9bd96b21cbfe", actual)

    [<Fact>]
    let ``Python control-character escaping remains byte compatible`` () =
        let b = String.replicate 64 "b"
        let files = file + "," + sprintf """{"path":"line\nbreak.md","sha256":"%s","executable":false}""" b
        let producerTree = "8c6239e593645ba79ef93f3c5eabc8e09b5c8a49b41d17bd308cfe87c9e1f414"
        let producer = (document (skill "work-roadmap" ".claude/skills/work-roadmap" files)).Replace(tree, producerTree)
        match DriverManifest.parse root producer with
        | Ok _ -> ()
        | Error messages -> failwithf "Python producer control escaping must be accepted: %A" messages
