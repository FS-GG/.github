namespace FS.GG.Telemetry.Tests

open System
open System.IO
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Telemetry.Host

[<CollectionDefinition("Host schema profile", DisableParallelization = true)>]
type HostSchemaProfileCollection() = class end

[<Collection("Host schema profile")>]
type HostSchemaProfileTests() =
    [<Fact>]
    member _.``current Host reports and restores only its bundled store schema`` () =
        let root = Path.Combine(Path.GetTempPath(), "host-current-profile-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        let assessment = TelemetryStore.ApprovedLocalDurable
        let workspace = "profile-workspace"
        let scope: TelemetryReceipt.Scope = { Workspace = workspace; Producer = "profile-producer"; Stream = "runtime" }
        let run argv =
            use output = new StringWriter()
            let original = Console.Out
            try
                Console.SetOut output
                let code = Operations.runWithAssessment argv (fun _ -> assessment)
                code, output.ToString()
            finally Console.SetOut original
        let privateFile (path: string) = File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        try
            let store = Path.Combine(root, "source")
            TelemetryStoreApplication.initialize store assessment |> Result.defaultWith (fun e -> failwithf "%A" e) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer store assessment scope |> Result.defaultWith (fun e -> failwithf "%A" e) |> ignore
            let sourceEnvelope = RemoteTelemetryTests.makeEnvelope scope "profile-batch" 0
            TelemetryStoreApplication.submitReceipt store assessment scope sourceEnvelope |> Result.defaultWith (fun e -> failwithf "%A" e) |> ignore
            TelemetryStoreApplication.drainReceipts store assessment workspace |> Result.defaultWith (fun e -> failwithf "%A" e) |> ignore
            let certificatePath = Path.Combine(root, "cert.pfx")
            let passwordPath = Path.Combine(root, "cert.pass")
            let browserPath = Path.Combine(root, "browser.json")
            use rsa = RSA.Create(2048)
            let request = CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            use certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1.), DateTimeOffset.UtcNow.AddDays(1.))
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-password"))
            File.WriteAllText(passwordPath, "test-password")
            File.WriteAllText(browserPath, """{"schema":"fsgg.telemetry.browser-key/1","algorithm":"sha256","keyHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")
            for path in [ certificatePath; passwordPath; browserPath ] do privateFile path
            let configFor (name: string) (storeRoot: string) =
                let path = Path.Combine(root, name + ".json")
                let config: HostConfig =
                    { Schema = "fsgg.telemetry.host-config/1"; ListenUrl = "https://127.0.0.1:18446"
                      CertificatePath = certificatePath; CertificatePasswordFile = passwordPath
                      ServiceLockPath = Path.Combine(root, "host.lock")
                      Stores = [| { WorkspaceId = workspace; Root = storeRoot } |]; Credentials = [||]
                      BrowserPrincipals = [| { PrincipalId = "profile-reader"; KeyHashFile = browserPath; WorkspaceIds = [| workspace |]; Revoked = false } |]
                      BrowserSession = RemoteTelemetryTests.browserSession }
                File.WriteAllText(path, JsonSerializer.Serialize config)
                privateFile path
                path
            let config = configFor "source-config" store
            for operation in [ "status"; "preflight" ] do
                let code, output = run [| operation; "--config"; config |]
                Assert.Equal(0, code)
                use result = JsonDocument.Parse output
                Assert.Equal(14, result.RootElement.GetProperty("supportedStoreSchemaMin").GetInt32())
                Assert.Equal(14, result.RootElement.GetProperty("supportedStoreSchemaMax").GetInt32())
            let backup = Path.Combine(root, "backup")
            let backupCode, _ = run [| "backup"; "--config"; config; "--output"; backup |]
            Assert.Equal(0, backupCode)
            let manifestPath = Path.Combine(backup, "backup-manifest.json")
            let manifestBytes = File.ReadAllBytes manifestPath
            use manifest = JsonDocument.Parse manifestBytes
            Assert.Equal("0.5.0", manifest.RootElement.GetProperty("hostVersion").GetString())
            Assert.Equal(14, manifest.RootElement.GetProperty("supportedStoreSchemaMin").GetInt32())
            Assert.Equal(14, manifest.RootElement.GetProperty("supportedStoreSchemaMax").GetInt32())
            let innerPath = Path.Combine(backup, workspace, "manifest.json")
            let innerBytes = File.ReadAllBytes innerPath
            let backupDatabase = Path.Combine(backup, workspace, TelemetryStoreApplication.databaseFileName)
            let sourceDigest = SHA256.HashData(File.ReadAllBytes backupDatabase)
            for minimum, maximum in [ 9, 9; 10, 10; 10, 12; 13, 13; 14, 15 ] do
                let target = Path.Combine(root, sprintf "refused-%d-%d" minimum maximum)
                let altered = JsonNode.Parse manifestBytes
                altered.["supportedStoreSchemaMin"] <- JsonValue.Create minimum
                altered.["supportedStoreSchemaMax"] <- JsonValue.Create maximum
                File.WriteAllText(manifestPath, altered.ToJsonString())
                let code, _ = run [| "restore"; "--config"; configFor (sprintf "refused-config-%d-%d" minimum maximum) (Path.Combine(target, workspace)); "--input"; backup; "--state-root"; target |]
                Assert.Equal(3, code)
                Assert.False(Directory.Exists target)
            // A schema14 outer label cannot authorize migration of an older inner store.
            let alteredInner = JsonNode.Parse innerBytes
            alteredInner.["storeSchemaVersion"] <- JsonValue.Create 9
            let alteredInnerBytes = JsonSerializer.SerializeToUtf8Bytes alteredInner
            File.WriteAllBytes(innerPath, alteredInnerBytes)
            let alteredOuter = JsonNode.Parse manifestBytes
            alteredOuter.["workspaces"].[0].["manifestSha256"] <- JsonValue.Create(Convert.ToHexString(SHA256.HashData alteredInnerBytes).ToLowerInvariant())
            File.WriteAllText(manifestPath, alteredOuter.ToJsonString())
            let refusedInner = Path.Combine(root, "refused-inner")
            let innerCode, _ = run [| "restore"; "--config"; configFor "inner-config" (Path.Combine(refusedInner, workspace)); "--input"; backup; "--state-root"; refusedInner |]
            Assert.Equal(3, innerCode)
            Assert.False(Directory.Exists refusedInner)
            File.WriteAllBytes(innerPath, innerBytes)
            File.WriteAllBytes(manifestPath, manifestBytes)
            let restored = Path.Combine(root, "restored")
            let restoredStore = Path.Combine(restored, workspace)
            let restoreCode, _ = run [| "restore"; "--config"; configFor "restore-config" restoredStore; "--input"; backup; "--state-root"; restored |]
            Assert.Equal(0, restoreCode)
            let receipt = TelemetryStoreApplication.lookupReceipt restoredStore assessment scope "profile-batch" |> Result.defaultWith (fun e -> failwithf "%A" e)
            Assert.Contains("\"status\":\"applied\"", receipt)
            let status = TelemetryStoreApplication.status restoredStore assessment |> Result.defaultWith (fun e -> failwithf "%A" e)
            use restoredStatus = JsonDocument.Parse status
            Assert.Equal(14, restoredStatus.RootElement.GetProperty("schemaVersion").GetInt32())
            Assert.Equal<byte>(sourceDigest, SHA256.HashData(File.ReadAllBytes backupDatabase))
        finally
            if Directory.Exists root then Directory.Delete(root, true)
