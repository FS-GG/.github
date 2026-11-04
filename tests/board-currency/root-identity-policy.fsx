// Credential-free component checks, no clock or network. Embedding is exercised by engine tests.
#load "../../src/FS.GG.Coord.Core/RootIdentityPolicy.fs"
open System
open System.IO
open System.Security.Cryptography
open System.Text
open FS.GG.Coord

let raw = File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "../../scripts/root-identity-policy.json"))
let accepted = RootIdentityPolicy.parse raw
match accepted with
| Error error -> failwith error
| Ok policy ->
    if policy.RootLogins <> set [ "EHotwagner" ] then failwith "Existing principal list changed"
    let digest = Convert.ToHexString(SHA256.HashData raw).ToLowerInvariant()
    if policy.Sha256 <> digest then failwith "Digest does not describe original canonical bytes"
    if policy.RootLogins.Contains "repository-admin" then failwith "Admin became Root"

let text = Encoding.UTF8.GetString raw
let controls = [
    "{"
    text.Replace("v1", "v9")
    text.Replace("[\"EHotwagner\"]", "[\"EHotwagner\",\"ehotwagner\"]")
    text.Replace("\"schema\":", "\"schema\":\"fsgg.root-identity-policy/v1\",\"schema\":")
    text.Replace("\"rootLogins\":", "\"admin\":true,\"rootLogins\":")
    text + " "
    text.Replace("[\"EHotwagner\"]", "[]")
    text.Replace("EHotwagner", "bad/name")
    text.Replace("[\"EHotwagner\"]", "[\"z\",\"a\"]")
]
for control in controls do
    match RootIdentityPolicy.parse (Encoding.UTF8.GetBytes control) with
    | Error _ -> ()
    | Ok _ -> failwith "Invalid policy accepted"
// FSI deliberately has no embedded resource: unavailable must refuse every public predicate.
match RootIdentityPolicy.policy, RootIdentityPolicy.rootLogins, RootIdentityPolicy.digest, RootIdentityPolicy.isRoot "EHotwagner" with
| Error _, Error _, Error _, Error _ -> ()
| _ -> failwith "Unavailable policy did not refuse"
printfn "Root identity policy: canonical policy accepted; %d invalid policies refused; missing embedded resource refused" controls.Length
