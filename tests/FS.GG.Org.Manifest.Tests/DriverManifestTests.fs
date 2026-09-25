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
