# LEARN W6 C2 served manager-bundle qualification

Date: 2026-10-02

## Verdict

**C2 is Closed at the served manager-bundle boundary.** The protected producer created one fresh
`bundle/2` artifact, read it back from Actions, and verified it against two independently acquired target
runtime trees. A separate read-only consumer then downloaded the served bytes, checked the closed manifest,
prepared receipt and archive bindings, reacquired the official SDK and target OCI inputs, reconstructed their
canonical inventories, and passed the exact `verify-v2` command.

This closure does not produce or qualify the inactive v3 receiver image. C3 image closure is next. C4 dual OCI
build comparison, C5 synthetic credential-free qualification, and C6 protected activation remain later gates.
No image was built or imported, no runtime was installed, no grant or private capture was used, no service was
started, and Main had no action.

## Protected source and served artifact

The producer is [FS-GG.Coordination #925](https://github.com/FS-GG/FS.GG.Coordination/pull/925), protected
commit `49fe964f0239ad3734f5fa5119b3227f2a04758d`, tree
`36b0be5cc74bfd10d045ab11ce978547464d5266`. The workflow SHA-256 is
`77f00b9016ce2e91fcafae196af1f0f9f73dffde313c2db93fc17fa36e8bfe4e`; its F# producer/verifier source
SHA-256 is `f2f89299abac9201c98a16c947008697e3638b9e026e760d2d4fa47c4f682805`. The legacy `/1` format and CLI
remain supported; this qualification consumes only the explicit `/2` successor.

[Protected run `36983338783`](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/36983338783) succeeded
for that exact commit. Its sole artifact is `11216418410`, named
`telemetry-host-manager-net10.0-49fe964f0239ad3734f5fa5119b3227f2a04758d`, with service size
`1536471`, SHA-256 `518041591b9b7f1827911f0e796a1815799841831b962d3112169d9241969cdd`, and expiry
`2026-12-31T08:19:03Z`. The downloaded artifact had the same SHA-256 and contained exactly the six expected
flat files.

The canonical `bundle/2` manifest SHA-256 is
`966e13e827b3b3a3f37c51bcc35fd57f4684741d19a3308905e5f66ac51ebf0e`; the prepared `/2` receipt SHA-256 is
`287ea427ec040f4fc4b3791fbea7cfb6dd765a9ee45584dacffd960cc10c5d1a`. The manager archive is `1465967`
bytes with SHA-256 `03d46e6553e99be27c0bbc06d8767e2aa2a5e9b588d608d4b4c229e7a87ad74f`. It has one byte-identical embedded
manifest plus 19 manager payloads; every payload length, SHA-256 and mode matched the manifest.

## Official OCI reconstruction

The target is `mcr.microsoft.com/dotnet/aspnet` for linux/amd64 at manifest
`sha256:ed6a2d26633ddcd3d42a1d9f9866214ecbbc11ba6ac5e0e843da02c13da24072` and config
`sha256:d84f2a8aca8b8dbf142dd6bb1ffa7a1c051085c55bf36fc2f7fa1b1f17820932`. Its normalized
`/usr/share/dotnet` tree has 337 files and 109735192 bytes with canonical inventory SHA-256
`ead4ece42719198be9607d18415e428e3a6fcaadf50b88dc6e93894c47bec4c2`: `dotnet` is mode `0555` and the
other 336 files are mode `0444`. It supplies Microsoft.NETCore.App and Microsoft.AspNetCore.App `10.0.12`.

The build input is `mcr.microsoft.com/dotnet/sdk` for linux/amd64 at manifest
`sha256:1aabdb4843de1c426d3676bf1220bc040e540f82a765320b3eb2c693e8d0a7dd` and config
`sha256:690de8d26a94a08b03190ccabf1906ac4025172267a1584255f062869caf8242`. Before the selected SDK executable
ran, system Python hashed all 4907 files and 640105059 bytes to canonical inventory SHA-256
`c51a26bcd972e5f1b2944a912ca57cab9878fa88a2d8110fa0300c54ba0afcb0`. The SDK is `10.0.400`; its selected
`dotnet` executable SHA-256 is `0a5ec28e49da2c0be91ff3fc8fff53c250c9bbd92b25d3b9bfc5721adba96a0c`.

An initial consumer invocation asked the SDK host for Microsoft.NETCore.App `10.0.12`. That host contains
`10.0.11`, so it refused before the verifier started. The protected workflow's canonical route runs the verifier
on the independently acquired target `10.0.12` host and supplies the separately admitted SDK tree for its exact
version and content checks. Repeating that route exited zero with
`TELEMETRY_HOST_MANAGER_BUNDLE_VERIFIED`. Both disposable acquisition containers were removed, and the isolated
image store, extracted trees and source checkout were deleted after the bounded evidence was retained.

## Remaining boundary

The historical artifact `11209647998` and its runtime-content and mode refusal remain immutable evidence; they
were not rewritten or reused. C2 establishes a reproducible served manager bundle and its official runtime/SDK
bindings. It does not establish an installed receiver, receiver-image reproducibility, genuine credential
custody, native capture, restart/recovery, enrollment, experiment activation, or C3 readiness.
